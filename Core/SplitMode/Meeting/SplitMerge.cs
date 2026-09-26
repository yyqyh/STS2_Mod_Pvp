using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PvpDuel.Core.SplitMode;

/// <summary>
/// 合流第三步：把两人拉进「决斗局」。
/// </summary>
/// <remarks>
/// <para>
/// 用的是本体「联机载入存档」那套（<c>RunSessionState.InLoadedLobby</c>），只是**不再重新连接**——
/// 我们那条 Steam 会话从分离期一开始就一直活着，所以自己驱动握手即可：
/// </para>
/// <list type="number">
/// <item><description>主机：<c>new LoadRunLobby(transport, listener, save)</c> → <c>AddLocalHostPlayer()</c> → <c>SetReady(true)</c>；</description></item>
/// <item><description>客户端：发 <c>ClientLoadJoinRequestMessage</c>，收到 <c>ClientLoadJoinResponseMessage</c>（里面带着同一份存档）后
/// <c>new LoadRunLobby(transport, listener, response)</c> → <c>SetReady(true)</c>；</description></item>
/// <item><description>两边都在 <see cref="SplitMergeListener.BeginRun" /> 里 <c>RunManager.SetUpSavedMultiplayer</c> + <c>NGame.LoadRun</c>，收尾 <c>CleanUp(disconnectSession: false)</c>。</description></item>
/// </list>
/// <para>
/// 这几步与本体 <c>NMultiplayerLoadGameScreen</c> 的做法逐行对应（那边是 109/128/157/207-229 行），
/// 区别只有两点：不重新连接、不弹"有玩家缺席"的确认框。
/// </para>
/// </remarks>
internal static class SplitMerge
{
    private static LoadRunLobby? _lobby;
    private static SplitMergeListener? _listener;

    private static bool _clientWaiting;
    private static double _sinceJoinRequest;

    /// <summary>等主机回执的耐心时间（超了只记日志，**不重发**）。</summary>
    private const double ClientJoinTimeoutSeconds = 15.0;

    /// <summary>已经在合流厅里了。</summary>
    public static bool InLobby => _lobby is not null;

    /// <summary>主机：带着合成好的决斗局存档开厅，自己先就位。</summary>
    public static bool StartHost(INetGameService transport, SerializableRun save)
    {
        if (_lobby is not null)
        {
            return false;
        }

        try
        {
            _listener = new SplitMergeListener();
            _lobby = new LoadRunLobby(transport, _listener, save);
            _lobby.AddLocalHostPlayer();
            _lobby.SetReady(true);

            // 关键一步：把我们这条传输的「消息缓冲」关掉。
            // 缓冲是联机局开局流程里打开的（RunManager.Launch 会关掉），而我们从来没跑过那个流程，
            // 于是它一直停在"开"——客户端的入厅请求会被 NetMessageBus 排队，日志里只有
            // "Received … but we are currently buffering messages."，处理器永远不执行。
            // 现在处理器已经注册好了，关掉缓冲会把排队中的请求立刻派发。
            transport.SetBufferMessages(false);

            ModInfo.Info(
                $"[SplitMode] 合流：主机已开「载入局」大厅（存档里 {save.Players.Count} 人，"
                + $"当前就位 {_lobby.PlayerCount} 人），等客户端");

            // 主动邀请客户端入厅：它收到才发一次请求。
            // （客户端自己重试会让主机重复加人——本体那个 handler 不去重，2026-09-26 实测刷到 32 人。）
            SplitMergeChannel.Initialize();
            SplitMergeChannel.BroadcastInvite(
                transport,
                new SplitMergeGoPayload(
                    save.Players.Count,
                    $"{save.Players[0].CharacterId?.Entry ?? "?"} vs {save.Players[1].CharacterId?.Entry ?? "?"}"));

            return true;
        }
        catch (Exception ex)
        {
            ModInfo.Error($"[SplitMode] 合流：主机开厅失败：{ex}");
            _lobby = null;
            return false;
        }
    }

    /// <summary>客户端：收到主机邀请后注册回执处理器，并**只发一次**入厅请求。</summary>
    public static void OnInviteReceived()
    {
        if (_lobby is not null || _clientWaiting)
        {
            return;
        }

        if (SplitLink.Transport is not { } transport)
        {
            return;
        }

        try
        {
            transport.RegisterMessageHandler<ClientLoadJoinResponseMessage>(OnClientLoadJoinResponse);
            _clientWaiting = true;
            _sinceJoinRequest = 0;

            // 客户端同样要关掉缓冲：不然主机回的 ClientLoadJoinResponseMessage 也会被排队。
            transport.SetBufferMessages(false);

            transport.SendMessage(default(ClientLoadJoinRequestMessage));

            ModInfo.Info("[SplitMode] 合流：已向主机发送入厅请求（只发一次）");
        }
        catch (Exception ex)
        {
            ModInfo.Error($"[SplitMode] 合流：客户端入厅请求失败：{ex}");
        }
    }

    /// <summary>由每帧的泵调用：只做"等太久"的提醒（不重发）。</summary>
    public static void Pump(double delta, INetGameService transport)
    {
        if (!_clientWaiting || _lobby is not null)
        {
            return;
        }

        _sinceJoinRequest += delta;

        if (_sinceJoinRequest < ClientJoinTimeoutSeconds)
        {
            return;
        }

        _sinceJoinRequest = 0;

        ModInfo.Warn(
            $"[SplitMode] 合流：等主机回执超过 {ClientJoinTimeoutSeconds:F0} 秒还没到"
            + "（不重发，免得主机重复加人；可以再走到同一个节点重试）");
    }

    private static void OnClientLoadJoinResponse(ClientLoadJoinResponseMessage message, ulong senderId)
    {
        if (_lobby is not null || SplitLink.Transport is not { } transport)
        {
            return;
        }

        try
        {
            transport.UnregisterMessageHandler<ClientLoadJoinResponseMessage>(OnClientLoadJoinResponse);
            _clientWaiting = false;

            _listener = new SplitMergeListener();
            _lobby = new LoadRunLobby(transport, _listener, message);
            _lobby.SetReady(true);

            ModInfo.Info(
                $"[SplitMode] 合流：收到主机存档（{message.serializableRun.Players.Count} 人），客户端已就位");
        }
        catch (Exception ex)
        {
            ModInfo.Error($"[SplitMode] 合流：客户端建厅失败：{ex}");
        }
    }

    /// <summary>两边在 <c>BeginRun</c> 回调里真正进局。</summary>
    internal static async Task EnterDuelRunAsync()
    {
        var lobby = _lobby;

        if (lobby is null)
        {
            return;
        }

        if (NGame.Instance is not { } game)
        {
            ModInfo.Warn("[SplitMode] 合流：拿不到 NGame（场景还没起来？），载入决斗局放弃");
            return;
        }

        try
        {
            var me = lobby.Run.Players.First(p => p.NetId == lobby.NetService.NetId);

            ModInfo.Info(
                $"[SplitMode] 合流：开始载入决斗局（我是 {me.CharacterId?.Entry ?? "?"}，局里 {lobby.Run.Players.Count} 人）");

            // 先把本机那局（分离期的单机局）收掉，SetUpSavedMultiplayer 要求 State 为空。
            if (RunManager.Instance is { IsInProgress: true })
            {
                RunManager.Instance.CleanUp();
            }

            var runState = RunState.FromSerializable(lobby.Run);

            await RunManager.Instance.SetUpSavedMultiplayer(runState, lobby);
            await game.LoadRun(runState, lobby.Run.PreFinishedRoom);

            lobby.CleanUp(disconnectSession: false);

            ModInfo.Info("[SplitMode] 合流：已经进到决斗局（同一局、两人都在）");
        }
        catch (Exception ex)
        {
            ModInfo.Error($"[SplitMode] 合流：载入决斗局失败：{ex}");
        }
    }
}

/// <summary>合流厅的回调（本体的载入局大厅要一个 listener）。</summary>
internal sealed class SplitMergeListener : ILoadRunLobbyListener
{
    public void PlayerConnected(LoadRunLobbyPlayer player)
    {
        ModInfo.Info($"[SplitMode] 合流：玩家就位 id={player.id}");
    }

    public void RemotePlayerDisconnected(ulong playerId)
    {
        ModInfo.Warn($"[SplitMode] 合流：玩家离开 id={playerId}");
    }

    public Task<bool> ShouldAllowRunToBegin()
    {
        // 不弹"有人缺席"确认框：两人到齐就开局。
        return Task.FromResult(true);
    }

    public void BeginRun()
    {
        ModInfo.Info("[SplitMode] 合流：双方都就位 → 开始载入");
        TaskHelper.RunSafely(SplitMerge.EnterDuelRunAsync());
    }

    public void PlayerReadyChanged(ulong playerId)
    {
    }

    public void LocalPlayerDisconnected(NetErrorInfo info)
    {
        ModInfo.Warn($"[SplitMode] 合流：本机被断开（{info.GetReason()}）");
    }
}
