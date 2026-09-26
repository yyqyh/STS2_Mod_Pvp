using System.Text.Json;

using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace PvpDuel.Core.SplitMode;

/// <summary>相遇：双方各自把「我这一局的玩家快照」发给对方（合流的第一半）。</summary>
/// <remarks>
/// <para>
/// <b>相遇发生在"进房间之前"</b>：只要两人同幕同坐标、<b>且都还站在地图上</b>（没进房间、没在战斗），
/// 就算相遇。理由：
/// ① 公平——两人都还没打这个节点，谁都没先打完再打你；
/// ② 不用等——若要求"房间打完"，对方可能在长战斗/长事件里待几分钟，另一边只能干站着；
/// ③ 干净——地图状态下没有进行中的房间与战斗，取快照、失败回滚都最省事。
/// </para>
/// <para>
/// 另外两条约束来自 0.8.0 的实测：同一张图（同种子）里两人的<b>出生点相同</b>，所以要把起始节点排除掉，
/// 否则单机局一开就"相遇"；以及同一个节点只触发一次（离开后再回来才算新的一次）。
/// </para>
/// <para>
/// 快照取的是本机单机局的 <c>SerializablePlayer</c>（<c>RunManager.ToSave(null)</c> 里那位玩家）——
/// 角色、卡组、遗物、药水、血量、金币都在里面。单机局里它的 <c>NetId</c> 恒为 1，
/// 所以主机合成时要把它改写成双方真实的网络 id（<c>LoadRunLobby</c> 就是按 netId 认人的）。
/// </para>
/// </remarks>
internal static class SplitMeeting
{
    private static SplitSnapshotPayload? _peerSnapshot;
    private static bool _builtDuelSave;

    /// <summary>同节点期间隔多久重发一次快照（两边触发差几帧也能凑齐，见下）。</summary>
    private const double ResendSeconds = 1.0;

    private static double _sinceSnapshot;

    /// <summary>我这条命发出去的那份快照 JSON（合成时要用；留到合成成功为止）。</summary>
    private static string? _localPlayerJson;

    /// <summary>上次触发过的「幕:列,行」，同一个节点不重复触发。</summary>
    private static string? _lastTriggeredNode;

    /// <summary>对端快照（收到后才有值）。</summary>
    public static SplitSnapshotPayload? PeerSnapshot => _peerSnapshot;

    /// <summary>现在算不算「相遇」：同幕同坐标 + 不是起始节点 + 该节点没触发过。</summary>
    /// <remarks>
    /// <b>不再要求"双方都站在地图上"</b>（0.9.0 那样定过，实测几乎打不出来：见 2026-09-26 那轮 log——
    /// 两人三次同坐标，每次都有至少一方已经进了房间/战斗，条件一次没满足）。
    /// 现在只要坐标相同就算相遇，哪怕一方正在自己的房间里：因为合流只用到玩家快照里的
    /// <c>Players</c>（角色/卡组/遗物/血量/金币），<b>不关心房间状态</b>——各自那一局的进度
    /// 会连房间一起存在自己的快照里，等打完决斗载回时继续。
    /// </remarks>
    public static bool ShouldTriggerMeeting()
    {
        if (!PresenceLedger.TryFindMeeting(out _, out _)
            || !PresenceLedger.TryGetLocal(out var mine))
        {
            return false;
        }

        if (RunManager.Instance?.DebugOnlyGetState() is not { } state)
        {
            return false;
        }

        // 起始节点不算相遇（同种子 → 两人出生点相同）。
        if (state.Map?.StartingMapPoint.coord is { } start
            && start.col == mine.MapColumn
            && start.row == mine.MapRow)
        {
            return false;
        }

        return _lastTriggeredNode != NodeKey(mine);
    }

    /// <summary>由每帧的泵调用。</summary>
    public static void Pump(double delta, INetGameService transport)
    {
        if (!ModuleSwitches.SplitMeeting)
        {
            return;
        }

        // 两端都要先把「合流邀请」通道注册好（Sub 是按 opcode 派发的，没注册就等于丢包）——
        // 0.15.0 只在主机侧注册，结果客户端根本收不到邀请。
        SplitMergeChannel.Initialize();

        _sinceSnapshot += delta;

        if (!ShouldTriggerMeeting())
        {
            // 分开了：允许下一次再相遇（已收到的快照留着，合成时还要用）。
            ResetMeeting();
        }
        else if (_sinceSnapshot >= ResendSeconds)
        {
            // 相遇期间**每秒重发一次**：两边触发时刻只差几帧、或者某一方那一下没判定成相遇
            // （比如正在战斗/转场），只发一次就会永远凑不齐——2026-09-26 实测就是这个
            // （节点 (2,1) 双方各发一次，彼此的都没到，这一次相遇就白费了）。
            TrySendSnapshot(transport);
        }

        // 合成**必须在主线程做**（Pump 就是主线程）：网络线程里碰 RunManager / ModelDb 是错的，
        // 而且 0.11.1 实测就是挂在网络线程上、悄悄什么都没发生。
        // 条件只看"两份快照齐了没"（不再依赖某次相遇的标记：两边触发差几帧就会被清掉）。
        if (!_builtDuelSave && _localPlayerJson is not null && _peerSnapshot is not null)
        {
            TryBuildDuelSave(transport);
        }
    }

    private static void ResetMeeting()
    {
        _sinceSnapshot = 0;
    }

    private static void TrySendSnapshot(INetGameService transport)
    {
        if (!PresenceLedger.TryFindMeeting(out var peerNetId, out _)
            || RunManager.Instance?.ToSave(null) is not { Players.Count: > 0 } save)
        {
            return;
        }

        SplitSnapshotChannel.Initialize();

        try
        {
            var me = save.Players[0];
            var json = JsonSerializer.Serialize(me);

            var payload = new SplitSnapshotPayload(
                json,
                me.CharacterId?.Entry ?? "?",
                me.Deck.Count,
                me.Relics.Count,
                me.NetId);

            if (!SplitSnapshotChannel.Send(transport, payload))
            {
                return;
            }

            _sinceSnapshot = 0;
            _localPlayerJson = json;
            PresenceLedger.TryGetLocal(out var mine);
            _lastTriggeredNode = NodeKey(mine);

            // 每秒都会重发，只在"这个节点第一次发"时打日志，免得刷屏。
            if (_lastTriggeredNode != NodeKey(mine))
            {
                ModInfo.Info(
                    $"[SplitMode] 相遇：在节点 act{mine.ActIndex}({mine.MapColumn},{mine.MapRow}) "
                    + $"把快照发给对端 {peerNetId}"
                    + $"（角色={payload.CharacterId} HP={me.CurrentHp}/{me.MaxHp} 卡组={payload.DeckSize} "
                    + $"遗物={payload.RelicCount} 药水={me.Potions.Count} JSON={json.Length} 字节）");
            }
        }
        catch (Exception ex)
        {
            ModInfo.Error($"[SplitMode] 相遇：取/发快照失败：{ex}");
        }
    }

    /// <summary>对端快照到了（可能在网络线程上调用）。</summary>
    public static void OnPeerSnapshot(ulong peerNetId, SplitSnapshotPayload payload)
    {
        _peerSnapshot = payload;

        ModInfo.Info(
            $"[SplitMode] 相遇：收到对端 {peerNetId} 的快照"
            + $"（角色={payload.CharacterId} 卡组={payload.DeckSize} 遗物={payload.RelicCount} "
            + $"JSON={payload.PlayerJson.Length} 字节）");

        // 收到对方快照就立刻回一份（不等下一次节流），这样"一边先发、另一边慢半拍"也能马上凑齐。
        _sinceSnapshot = ResendSeconds;
    }

    /// <summary>主机把两张快照合成「决斗局存档」（客户端不合成，它从 LoadRunLobby 拿同一份）。</summary>
    private static void TryBuildDuelSave(INetGameService transport)
    {
        if (_peerSnapshot is not { } peer || _localPlayerJson is not { } localJson)
        {
            return;
        }

        if (PresenceLedger.TryGetFirstPeer(out var peerNetId, out _) is false)
        {
            return;
        }

        var isHost = transport.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host;

        if (!isHost)
        {
            // 客户端不合成、也不自己发起：等主机的合流邀请（见 SplitMergeChannel 的注释）。
            return;
        }

        if (!DuelSaveBuilder.TryBuild(
                transport.NetId,
                peerNetId,
                localJson,
                peer.PlayerJson,
                out var duelSave,
                out var summary))
        {
            ModInfo.Warn($"[SplitMode] 合流：合成决斗局存档失败：{summary}");
            return;
        }

        _builtDuelSave = true;
        ModInfo.Info($"[SplitMode] 合流：{summary}");

        if (duelSave is not null && SplitMerge.StartHost(transport, duelSave))
        {
            ModInfo.Info("[SplitMode] 合流：存档已交给 LoadRunLobby，等客户端就位后开局");
        }
    }

    private static string NodeKey(SplitPresencePayload payload)
    {
        return $"{payload.ActIndex}:{payload.MapColumn},{payload.MapRow}";
    }
}

