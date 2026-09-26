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

    /// <summary>给启动日志用的一行摘要。</summary>
    public static string Describe()
    {
        return $"主={(Core ? "开" : "关")} 入口={(Invitation ? "开" : "关")} "
               + $"额外幕={(ExtraAct ? "开" : "关")} 调试入口={(DebugEntry ? "开" : "关")} "
               + $"解绑={(TogetherUnbind ? "开" : "关")} 平衡={(Balance ? "开" : "关")} "
               + $"横幅={(Banners ? "开" : "关")} "
               + $"诊断={(Diagnostics ? "开" : "关")}";
    }
}
