using System.Globalization;

using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace PvpDuel.Core.Settings;

/// <summary>设置界面：主开关 + 模块开关 + 平衡数值。</summary>
/// <remarks>
/// 局内改这些没有意义（章节列表在开局就定下来、决斗布置在战斗初始化时定下来），所以标成「局内只读」。
/// 带 ⚠ 的项会改变对局内容，<b>两端必须一致</b>；横幅 / 诊断是纯本机表现。
/// 平衡数值里填 0 一律表示"关掉那条规则"。
/// </remarks>
internal static class PvpModSettingsPage
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        RitsuLibFramework.RegisterModSettings(ModInfo.ModId, page => page
            .WithTitle(ModSettingsText.Literal("PVP 决斗"))
            .WithModDisplayName(ModSettingsText.Literal("PVP 决斗"))
            .WithReadOnlyOnHostSurfaces(ModSettingsHostSurface.RunPause | ModSettingsHostSurface.CombatPause)
            .AddSection("duel", section => section
                .WithTitle(ModSettingsText.Literal("决斗"))
                .AddToggle(
                    "enabled",
                    ModSettingsText.Literal("主开关：开启决斗模式"),
                    Toggle(s => s.Enabled, (s, v) => s.Enabled = v),
                    ModSettingsText.Literal("关掉＝本 mod 所有模块都不工作。决斗只在「正好两名玩家」的局里生效。"))
                .AddString(
                    "max_rounds",
                    ModSettingsText.Literal("回合上限（1~999）"),
                    Number(s => s.MaxRounds, (s, v) => s.MaxRounds = v, 1, 999),
                    placeholder: ModSettingsText.Literal("例如 30"),
                    maxLength: 3,
                    description: ModSettingsText.Literal(
                        "打满这么多回合还没分胜负，就按剩余血量百分比判定：血多的一方赢。\n"
                        + "用百分比是因为两人可以选不同角色、最大生命可能差很多。想快速验证可以填 3。"),
                    valueValidationVisual: InRange(1, 999)))
            .AddSection("balance", section => section
                .WithTitle(ModSettingsText.Literal("平衡"))
                .AddParagraph(
                    "balance_hint",
                    ModSettingsText.Literal(
                        "这些是「限制」类规则，都只在决斗里生效，填 0 = 关掉那条规则。（⚠ 两端要一致）\n"
                        + "「无限」有两条路：回合内无限（连击/循环）用前两条堵，回合数无限（额外回合）用第三条堵。"))
                .AddString(
                    "max_cards_per_turn",
                    ModSettingsText.Literal("单回合出牌上限（0 = 不限制）"),
                    Number(s => s.MaxCardsPerTurn, (s, v) => s.MaxCardsPerTurn = v, 0, 99),
                    placeholder: ModSettingsText.Literal("例如 15"),
                    maxLength: 2,
                    description: ModSettingsText.Literal(
                        "一个回合内打出这么多张牌就自动结束你的回合（本体「时间吞噬者」就是这么卡 12 张的）。"),
                    valueValidationVisual: InRange(0, 99))
                .AddString(
                    "max_same_card_per_turn",
                    ModSettingsText.Literal("同一张牌每回合上限（0 = 不限制）"),
                    Number(s => s.MaxSameCardPerTurn, (s, v) => s.MaxSameCardPerTurn = v, 0, 99),
                    placeholder: ModSettingsText.Literal("例如 3"),
                    maxLength: 2,
                    description: ModSettingsText.Literal(
                        "同一张牌本回合打够这么多下之后就打不出去了，专治「两张牌互刷」的经典无限。\n"
                        + "按「这张牌本身」算，不是按牌名 —— 同名卡的不同副本各自计数。"),
                    valueValidationVisual: InRange(0, 99))
                .AddString(
                    "extra_turn_limit",
                    ModSettingsText.Literal("连续额外回合上限（0 = 不限制）"),
                    Number(s => s.ExtraTurnLimit, (s, v) => s.ExtraTurnLimit = v, 0, 99),
                    placeholder: ModSettingsText.Literal("例如 1"),
                    maxLength: 2,
                    description: ModSettingsText.Literal(
                        "本体发起的「额外回合」（佩尔之眼那类效果）连着给同一个人的次数上限，超了就不给。"),
                    valueValidationVisual: InRange(0, 99))
                .AddString(
                    "second_energy",
                    ModSettingsText.Literal("后手补偿：额外能量（0 = 不给）"),
                    Number(s => s.SecondPlayerEnergy, (s, v) => s.SecondPlayerEnergy = v, 0, 9),
                    placeholder: ModSettingsText.Literal("例如 1"),
                    maxLength: 1,
                    description: ModSettingsText.Literal("后手方（大厅顺序里的第二位）在自己第一个回合多拿这么多能量。"),
                    valueValidationVisual: InRange(0, 9))
                .AddString(
                    "second_block",
                    ModSettingsText.Literal("后手补偿：开局格挡（0 = 不给）"),
                    Number(s => s.SecondPlayerBlock, (s, v) => s.SecondPlayerBlock = v, 0, 99),
                    placeholder: ModSettingsText.Literal("例如 5"),
                    maxLength: 2,
                    description: ModSettingsText.Literal(
                        "后手方在自己第一个回合开场获得这么多格挡 —— 用来吃掉先手第一轮的爆发。"),
                    valueValidationVisual: InRange(0, 99))
                .AddString(
                    "second_cards",
                    ModSettingsText.Literal("后手补偿：额外抽牌（0 = 不给）"),
                    Number(s => s.SecondPlayerCards, (s, v) => s.SecondPlayerCards = v, 0, 9),
                    placeholder: ModSettingsText.Literal("默认 0"),
                    maxLength: 1,
                    description: ModSettingsText.Literal(
                        "后手方第一个回合多抽几张。默认给 0：抽牌对循环类卡组收益过大，和上面的限制会打架。"),
                    valueValidationVisual: InRange(0, 9)))
            .AddSection("modules", section => section
                .WithTitle(ModSettingsText.Literal("模块"))
                .AddToggle(
                    "module_invitation",
                    ModSettingsText.Literal("入口：生成「对决邀请」"),
                    Toggle(s => s.ModuleInvitation, (s, v) => s.ModuleInvitation = v),
                    ModSettingsText.Literal("关掉＝本 mod 没有任何入口。（⚠ 两端要一致）"))
                .AddToggle(
                    "module_extra_act",
                    ModSettingsText.Literal("额外幕：三幕打完进「PVP 决斗」幕"),
                    Toggle(s => s.ModuleExtraAct, (s, v) => s.ModuleExtraAct = v),
                    ModSettingsText.Literal(
                        "正式入口。关掉后事件触发了也不会多一幕（适合只想用调试入口跑图）。\n"
                        + "⚠ 会改变本局的章节数量，两端不一致会在第三幕结束处分叉。"),
                    visibleWhen: () => ModuleSwitches.Invitation)
                .AddToggle(
                    "module_debug_entry",
                    ModSettingsText.Literal("调试入口：本局第一个问号房"),
                    Toggle(s => s.ModuleDebugEntry, (s, v) => s.ModuleDebugEntry = v),
                    ModSettingsText.Literal("把第一个问号房直接换成对决邀请，省得跑完三幕。\n⚠ 两端要一致。"),
                    visibleWhen: () => ModuleSwitches.Invitation)
                .AddToggle(
                    "module_together_unbind",
                    ModSettingsText.Literal("进决斗前解绑 together 的共享"),
                    Toggle(s => s.ModuleTogetherUnbind, (s, v) => s.ModuleTogetherUnbind = v),
                    ModSettingsText.Literal(
                        "决斗是各自为战，所以开打前会断开共生体共享、把共享卡组按奇偶分给两边。\n"
                        + "together 没装 / 没配对时什么都不会发生。（⚠ 会影响共享状态）"))
                .AddToggle(
                    "module_balance",
                    ModSettingsText.Literal("平衡：上限与后手补偿"),
                    Toggle(s => s.ModuleBalance, (s, v) => s.ModuleBalance = v),
                    ModSettingsText.Literal("上面「平衡」那一节的规则总开关。（⚠ 两端要一致）"))
                .AddToggle(
                    "module_banners",
                    ModSettingsText.Literal("改写回合横幅文案"),
                    Toggle(s => s.ModuleBanners, (s, v) => s.ModuleBanners = v),
                    ModSettingsText.Literal("把「额外回合」改回「玩家回合」、并去掉空跑的「敌方回合」。（纯本机表现）"))
                .AddToggle(
                    "module_limit_hud",
                    ModSettingsText.Literal("决斗限制小抄（右上角）"),
                    Toggle(s => s.ModuleLimitHud, (s, v) => s.ModuleLimitHud = v),
                    ModSettingsText.Literal(
                        "战斗右上角显示：回合数 / 双方本回合出牌用量 / 同卡用量 / 双方血量百分比与差 / 后手补偿状态。\n"
                        + "纯本机表现，不参与判定。（位置在 Core/Duel/Hud/DuelHud.cs 里一行可调）"))
                .AddToggle(
                    "module_diagnostics",
                    ModSettingsText.Literal("详细日志与异常诊断"),
                    Toggle(s => s.ModuleDiagnostics, (s, v) => s.ModuleDiagnostics = v),
                    ModSettingsText.Literal(
                        "打开后：只打一次的过程日志（站位、目标重算、拦怪等）会写进 godot.log，\n"
                        + "并把战斗启动时被本体吞掉的异常完整打出来。排查完可以关掉。（纯本机）")))
            .AddSection("split", section => section
                .WithTitle(ModSettingsText.Literal("分离模式（实验）"))
                .AddParagraph(
                    "split_hint",
                    ModSettingsText.Literal(
                        "目标：两人各跑各的单机局（同种子），地图上只同步对方的「位置图标」，相遇时才合流进一局打 PVP。\n"
                        + "目前只做了地基：把那条联机传输从「一局」里剥出来并保活，还没有任何玩法改动。\n"
                        + "验证步骤与后续路线见工程里的 docs/SPLIT_MODE.md。（⚠ 实验功能，两端都要开）"))
                .AddToggle(
                    "module_split_mode",
                    ModSettingsText.Literal("分离模式：接管联机链路"),
                    Toggle(s => s.ModuleSplitMode, (s, v) => s.ModuleSplitMode = v),
                    ModSettingsText.Literal(
                        "开着才会捕获并保留那条联机传输（日志前缀 [SplitMode]）。关掉＝完全不碰联机逻辑。"))
                .AddToggle(
                    "module_split_keep_alive",
                    ModSettingsText.Literal("分离模式：退出对局不断线（保活）"),
                    Toggle(s => s.ModuleSplitKeepAlive, (s, v) => s.ModuleSplitKeepAlive = v),
                    ModSettingsText.Literal(
                        "退出 / 放弃对局时不主动断开联机连接，把连接留给分离期用。\n"
                        + "只拦「退出对局」这一次，在大厅里按「离开联机」照旧正常断开；\n"
                        + "想彻底退联机：先把这个开关关掉再退。"),
                    visibleWhen: () => ModuleSwitches.SplitMode)
                .AddToggle(
                    "module_split_presence",
                    ModSettingsText.Literal("分离模式：同步对方坐标（Presence）"),
                    Toggle(s => s.ModuleSplitPresence, (s, v) => s.ModuleSplitPresence = v),
                    ModSettingsText.Literal(
                        "每 250ms 把「我在第几幕的哪个坐标、在地图/房间/战斗」发给对端，对端的状态行会显示出来。\n"
                        + "这是分离期唯一的业务流量，走 RitsuLib 的 Sidecar 通道。（⚠ 两端都要开）"),
                    visibleWhen: () => ModuleSwitches.SplitMode)
                .AddToggle(
                    "module_split_icon",
                    ModSettingsText.Literal("分离模式：在地图上画出对方"),
                    Toggle(s => s.ModuleSplitIcon, (s, v) => s.ModuleSplitIcon = v),
                    ModSettingsText.Literal(
                        "地图上给对方所在的那个节点贴一个小标记（`◆ 对端 83%`；两人同节点时变成 `⚔ 相遇`）。\n"
                        + "纯本机表现，不参与判定。"),
                    visibleWhen: () => ModuleSwitches.SplitMode)
                .AddToggle(
                    "module_split_solo_run",
                    ModSettingsText.Literal("分离模式：大厅按开始时，各自开自己的单机局"),
                    Toggle(s => s.ModuleSplitSoloRun, (s, v) => s.ModuleSplitSoloRun = v),
                    ModSettingsText.Literal(
                        "开（分离模式）：在角色选择界面按下开始时拦下，给本机开一局自己的单机局（两人同一种子）。\n"
                        + "　大厅连接不会被断，两人各跑各的、地图一致，地图上能看到对方在别的节点。\n"
                        + "关（老玩法）：照常开联机共享局，三幕打完进「PVP 决斗」额外幕，在「对决邀请」事件里开打。\n"
                        + "⚠ 实验功能：开着时卡组/遗物是初始的（不继承进度）、两人各自独立，\n"
                        + "在合流做完之前回不到同一局——想恢复原样请重开游戏。（⚠ 两端都要开）"),
                    visibleWhen: () => ModuleSwitches.SplitMode)
                .AddToggle(
                    "module_split_meeting",
                    ModSettingsText.Literal("相遇决斗：走到同一节点（进房间前）就交换快照"),
                    Toggle(s => s.ModuleSplitMeeting, (s, v) => s.ModuleSplitMeeting = v),
                    ModSettingsText.Literal(
                        "只属于分离模式：开着时，两人走到同一节点【进房间之前】各发一份自己的玩家快照，"
                        + "为「合流进一局打 PVP」做准备（目前只交换并打日志）。\n"
                        + "关掉＝分离期就只是各玩各的（仍然同步坐标、地图上能看到对方），不会触发任何对决。\n"
                        + "⚠ 分离模式关掉时这一项不生效（老玩法的 PVP 入口是额外幕 + 事件）。（⚠ 两端都要开）"),
                    visibleWhen: () => ModuleSwitches.SplitMode)
                .AddToggle(
                    "module_split_status_hud",
                    ModSettingsText.Literal("分离模式：状态行（左上角）"),
                    Toggle(s => s.ModuleSplitStatusHud, (s, v) => s.ModuleSplitStatusHud = v),
                    ModSettingsText.Literal(
                        "左上角一行小字：当前阶段 / 链路存活 / 本机 netId / 对端 id。纯本机观测，不参与判定。\n"
                        + "（改这项需要重启游戏才生效）")))
            .AddSection("how", section => section
                .WithTitle(ModSettingsText.Literal("流程"))
                .AddParagraph(
                    "how_it_works",
                    ModSettingsText.Literal(
                        "接受对决后这一场进入决斗：\n"
                        + "1. 没有怪物，两人各自用自己的角色与卡组；\n"
                        + "2. 一人一回合轮流行动（对手的立绘在对面，手牌留到自己回合）；\n"
                        + "3. 谁先倒下谁输；打满回合上限则按剩余血量百分比判定；\n"
                        + "4. 决斗是一局定胜负，分出结果后直接结算这一局。\n"
                        + "单人局、三人局下本 mod 什么都不做。"))));
    }

    /// <summary>整数设置项：显示与写回都带上下限。</summary>
    private static ModSettingsValueBinding<PvpSettings, string> Number(
        Func<PvpSettings, int> read,
        Action<PvpSettings, int> write,
        int min,
        int max)
    {
        return new ModSettingsValueBinding<PvpSettings, string>(
            ModInfo.ModId,
            PvpSettingsStore.DataKey,
            SaveScope.Global,
            settings => Math.Clamp(read(settings), min, max).ToString(CultureInfo.InvariantCulture),
            (settings, value) =>
            {
                if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    write(settings, Math.Clamp(parsed, min, max));
                }
            });
    }

    private static ModSettingsValueBinding<PvpSettings, bool> Toggle(
        Func<PvpSettings, bool> read,
        Action<PvpSettings, bool> write)
    {
        return new ModSettingsValueBinding<PvpSettings, bool>(
            ModInfo.ModId,
            PvpSettingsStore.DataKey,
            SaveScope.Global,
            read,
            write);
    }

    /// <summary>输入框的即时校验（填了范围外的值只标红，不写回）。</summary>
    private static Func<string?, bool> InRange(int min, int max)
    {
        return value =>
            int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= min
            && parsed <= max;
    }
}
