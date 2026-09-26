namespace PvpDuel.Core.SplitMode;

/// <summary>分离模式（C2）的阶段。骨架先立着，后续每一步只填一个阶段。</summary>
internal enum SplitStage
{
    /// <summary>没启用（或已经回到普通玩法）。</summary>
    Off,

    /// <summary>分离期：本机跑自己的单机局，跨机只传坐标与信令。</summary>
    SoloRun,

    /// <summary>相遇：握手 → 取快照 → 合成 PVP 局存档 → 切会话。</summary>
    Meeting,

    /// <summary>决斗期：临时的联机局，只装「PVP 幕」。</summary>
    DuelRun,

    /// <summary>赛后：输家结束本局、赢家载回自己的快照。</summary>
    AfterRun,
}

/// <summary>分离模式的阶段状态（唯一一份，只在日志与 HUD 里读）。</summary>
internal static class SplitRuntime
{
    public static SplitStage Stage { get; private set; } = SplitStage.Off;

    public static void SetStage(SplitStage stage)
    {
        if (Stage == stage)
        {
            return;
        }

        ModInfo.Info($"[SplitMode] 阶段 {Stage} → {stage}");
        Stage = stage;
    }

    public static string Describe()
    {
        return Stage switch
        {
            SplitStage.Off => "未启用",
            SplitStage.SoloRun => "分离期（各自单机局）",
            SplitStage.Meeting => "相遇（握手 / 合成存档）",
            SplitStage.DuelRun => "决斗期（临时联机局）",
            SplitStage.AfterRun => "赛后（结束 / 载回）",
            _ => Stage.ToString(),
        };
    }
}
