using Godot;

namespace PvpDuel.Core.SplitMode;

/// <summary>本机与对端最近一次的坐标台账（分离期画图标、判「相遇」都读它）。</summary>
/// <remarks>
/// 写入可能来自网络线程（收到消息）或主线程（读到自己的位置），所以一律加锁；
/// 时间戳只由主线程的泵来盖（<see cref="NoteMainThreadPump" />），因为接收线程里不该碰 Godot 的时钟。
/// </remarks>
internal static class PresenceLedger
{
    private static readonly object Gate = new();

    private static readonly Dictionary<ulong, PeerEntry> PeerMap = [];

    private static SplitPresencePayload _local;
    private static bool _localKnown;

    /// <summary>收到过消息的总次数（接收线程自增，主线程用它判断"有没有新东西"）。</summary>
    private static int _receiveRevision;

    private static int _stampedRevision;
    private static int _lastLoggedRevision;

    private sealed class PeerEntry
    {
        public SplitPresencePayload Payload;
        public ulong ReceivedMsec;

        /// <summary>上次打日志时的"位置指纹"：只有坐标/活动真变了才再打一行，免得 4 条/秒刷屏。</summary>
        public string LoggedPlace = "";
    }

    /// <summary>记下本机的位置（主线程，来自泵）。</summary>
    public static void SetLocal(SplitPresencePayload payload)
    {
        lock (Gate)
        {
            _local = payload;
            _localKnown = true;
        }
    }

    /// <summary>对端发来的位置（可能来自网络线程）。</summary>
    public static void Apply(ulong peerNetId, SplitPresencePayload payload)
    {
        lock (Gate)
        {
            if (!PeerMap.TryGetValue(peerNetId, out var entry))
            {
                entry = new PeerEntry();
                PeerMap[peerNetId] = entry;
            }

            entry.Payload = payload;
            _receiveRevision++;
        }
    }

    /// <summary>主线程每帧调一次：给刚收到的消息盖时间戳，并按需打一行日志。</summary>
    public static void NoteMainThreadPump()
    {
        int revision;
        List<(ulong Peer, SplitPresencePayload Payload, int Total)> fresh = [];

        lock (Gate)
        {
            revision = _receiveRevision;

            if (revision != _stampedRevision)
            {
                _stampedRevision = revision;
                var now = Now();

                foreach (var entry in PeerMap.Values)
                {
                    entry.ReceivedMsec = now;
                }
            }

            if (revision == _lastLoggedRevision)
            {
                return;
            }

            _lastLoggedRevision = revision;

            foreach (var (peer, entry) in PeerMap)
            {
                var place = PlaceKey(entry.Payload);

                if (place == entry.LoggedPlace)
                {
                    continue;
                }

                entry.LoggedPlace = place;
                fresh.Add((peer, entry.Payload, revision));
            }
        }

        foreach (var (peer, payload, total) in fresh)
        {
            ModInfo.Info($"[SplitMode] Presence 收到对端 {peer}：{DescribePayload(payload)}（累计 {total} 条）");
        }
    }

    /// <summary>本机位置（画图标 / 判相遇用）。</summary>
    public static bool TryGetLocal(out SplitPresencePayload payload)
    {
        lock (Gate)
        {
            payload = _local;
            return _localKnown;
        }
    }

    /// <summary>随便取一个已收到过坐标的对端（两人局里就是那一位）。</summary>
    public static bool TryGetFirstPeer(out ulong peerNetId, out SplitPresencePayload payload)

    {
        peerNetId = 0;
        payload = default;

        lock (Gate)
        {
            foreach (var (peer, entry) in PeerMap)
            {
                peerNetId = peer;
                payload = entry.Payload;
                return true;
            }
        }

        return false;
    }

    /// <summary>相遇判定：同幕 + 同坐标（对端还没选过点不算）。</summary>
    public static bool TryFindMeeting(out ulong peerNetId, out SplitPresencePayload peerPayload)
    {
        peerNetId = 0;
        peerPayload = default;

        lock (Gate)
        {
            if (!_localKnown || !_local.HasCoord)
            {
                return false;
            }

            foreach (var (peer, entry) in PeerMap)
            {
                if (!entry.Payload.HasCoord
                    || entry.Payload.ActIndex != _local.ActIndex
                    || entry.Payload.MapColumn != _local.MapColumn
                    || entry.Payload.MapRow != _local.MapRow)
                {
                    continue;
                }

                peerNetId = peer;
                peerPayload = entry.Payload;
                return true;
            }
        }

        return false;
    }

    /// <summary>一行状态：我 + 每个对端（坐标 / 状态 / 多久前收到）。</summary>
    public static string Describe()
    {
        lock (Gate)
        {
            var mine = _localKnown ? DescribePayload(_local) : "未知";
            var builder = new System.Text.StringBuilder($"我 {mine}");

            if (PeerMap.Count == 0)
            {
                builder.Append("｜对端 未知");
            }
            else
            {
                var now = Now();

                foreach (var (peer, entry) in PeerMap)
                {
                    var ageSeconds = (now - entry.ReceivedMsec) / 1000.0;
                    builder.Append($"｜对端 {peer} {DescribePayload(entry.Payload)}（{ageSeconds:F1}s 前）");
                }
            }

            return builder.ToString();
        }
    }

    private static string DescribePayload(SplitPresencePayload payload)
    {
        var coord = payload.HasCoord
            ? $"act{payload.ActIndex}({payload.MapColumn},{payload.MapRow})"
            : $"act{payload.ActIndex} 未选点";

        var activity = payload.Activity switch
        {
            0 => "地图",
            1 => "房间",
            2 => "战斗",
            3 => "已结束",
            _ => "?",
        };

        return $"{coord} {activity} {payload.HpPercent}%";
    }

    /// <summary>用来判断"位置有没有真的变"的指纹（不含血量：血量在战斗里每帧都在动）。</summary>
    private static string PlaceKey(SplitPresencePayload payload)
    {
        return $"{payload.ActIndex}/{payload.HasCoord}/{payload.MapColumn}/{payload.MapRow}/{payload.Activity}";
    }

    private static ulong Now()
    {
        return Time.GetTicksMsec();
    }
}
