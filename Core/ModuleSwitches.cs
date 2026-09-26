using PvpDuel.Core.Settings;

namespace PvpDuel.Core;

/// <summary>模块级开关：每个模块的补丁在入口处先问这里，关掉的模块连补丁体都不会执行。</summary>
/// <remarks>
/// <para>
/// 两类开关别混：① 会改变对局内容的（入口 / 额外幕 / 调试入口 / 解绑）——两端必须一致，
/// 否则章节列表或共享状态会分叉；② 纯本机可见的（横幅 / 诊断）——各机可以不同。
/// </para>
/// <para>
/// 依赖关系：额外幕与调试入口都建立在「入口」模块之上（都要靠那个事件进决斗），
/// 所以关掉入口 = 这两个跟着失效；所有模块（诊断除外）都受主开关 <see cref="Core" /> 约束。
/// 诊断独立于主开关：开关关着也要能看见「为什么不生效」。
/// </para>
/// <para>「模块」在本工程里是一等公民：一个模块 = 一个目录 + 这里的一个开关 + 它的补丁与设置项。</para>
/// </remarks>
internal static class ModuleSwitches
{
    private static PvpSettings Settings => PvpSettingsStore.Current;

    /// <summary>主开关：决斗本体（战斗改造、轮流行动、目标判定、回合上限、结算）。</summary>
    public static bool Core => Settings.Enabled;

    /// <summary>入口模块：「对决邀请」事件。（⚠ 两端一致）</summary>
    public static bool Invitation => Core && Settings.ModuleInvitation;

    /// <summary>额外幕模块：三幕打完进入「PVP 决斗」幕。（⚠ 两端一致）</summary>
    public static bool ExtraAct => Invitation && Settings.ModuleExtraAct;

    /// <summary>调试入口：本局第一个问号房直接进决斗。（⚠ 两端一致）</summary>
    public static bool DebugEntry => Invitation && Settings.ModuleDebugEntry;

    /// <summary>互操作模块：进决斗前解绑 together 的共享。（⚠ 会影响共享状态）</summary>
    public static bool TogetherUnbind => Core && Settings.ModuleTogetherUnbind;

    /// <summary>表现模块：决斗里的回合横幅文案。</summary>
    public static bool Banners => Core && Settings.ModuleBanners;

    /// <summary>表现模块：决斗限制小抄（右上角）。</summary>
    public static bool LimitHud => Core && Settings.ModuleLimitHud;

    /// <summary>诊断模块：只打一次的详细日志 + 战斗启动异常打印。</summary>
    public static bool Diagnostics => Settings.ModuleDiagnostics;

    /// <summary>平衡模块：单回合出牌上限、同卡上限、额外回合上限、后手补偿。（⚠ 两端一致）</summary>
    public static bool Balance => Core && Settings.ModuleBalance;

    /// <summary>
    /// 分离模式（C2 实验）：把联机链路从「一局」里剥出来，为「两人各跑各的单机局 + 相遇才合流」打地基。
    /// </summary>
    /// <remarks>目前只做地基（保活链路 + 状态观测），还没有任何玩法改动。设计见 <c>docs/SPLIT_MODE.md</c>。</remarks>
    public static bool SplitMode => Core && Settings.ModuleSplitMode;

    /// <summary>分离模式的「保活」：退出对局时不主动断开联机连接。（⚠ 实验功能，两端都要开）</summary>
    public static bool SplitKeepAlive => SplitMode && Settings.ModuleSplitKeepAlive;

    /// <summary>分离模式的坐标同步（Presence 通道）：每隔 250ms 把「我在哪、在干什么」发给对端。（⚠ 两端一致）</summary>
    public static bool SplitPresence => SplitMode && Settings.ModuleSplitPresence;

    /// <summary>分离模式的地图标记：在地图上把对端画出来。（纯本机表现）</summary>
    public static bool SplitIcon => SplitMode && Settings.ModuleSplitIcon;

    /// <summary>
    /// <b>分离模式</b>：大厅按下开始时各自开自己的单机局（同种子），连接保留。
    /// </summary>
    /// <remarks>
    /// 关掉＝老玩法：照常开联机共享局，三幕打完进「PVP 决斗」额外幕、在事件里开打。
    /// ⚠ 实验功能：开着时两人各自独立，没有合流之前回不去同一局。（⚠ 两端一致）
    /// </remarks>
    public static bool SplitSoloRun => SplitMode && Settings.ModuleSplitSoloRun;

    /// <summary>
    /// <b>相遇决斗</b>：分离期里走到同一节点（进房间之前）时双方交换玩家快照，为合流开打做准备。
    /// </summary>
    /// <remarks>
    /// 和「分离模式」是两件事：分离模式决定"这局长什么样"（各自单机局 / 联机共享局），
    /// 相遇决斗只是在分离期上加的一条玩法。所以它从属于分离模式——分离模式关掉时它自然也不生效。
    /// （⚠ 两端一致）
    /// </remarks>
    public static bool SplitMeeting => SplitSoloRun && Settings.ModuleSplitMeeting;

    /// <summary>分离模式的状态行（纯本机观测，和诊断一样不受主开关约束）。</summary>
    public static bool SplitStatusHud => Settings.ModuleSplitStatusHud;

    /// <summary>给启动日志用的一行摘要。</summary>
    public static string Describe()
    {
        return $"主={(Core ? "开" : "关")} 入口={(Invitation ? "开" : "关")} "
               + $"额外幕={(ExtraAct ? "开" : "关")} 调试入口={(DebugEntry ? "开" : "关")} "
               + $"解绑={(TogetherUnbind ? "开" : "关")} 平衡={(Balance ? "开" : "关")} "
               + $"横幅={(Banners ? "开" : "关")} 分离模式={(SplitMode ? "开" : "关")} "
               + $"诊断={(Diagnostics ? "开" : "关")}";
    }
}
