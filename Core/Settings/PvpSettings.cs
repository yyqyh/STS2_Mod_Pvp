namespace PvpDuel.Core.Settings;

/// <summary>本 mod 的持久化设置（加字段给个合理默认值即可，老配置缺字段会取默认值）。</summary>
/// <remarks>
/// 带「⚠ 两端一致」的开关会改变对局内容（章节列表、房间、共享状态），两端不一致会分叉；
/// 其余是纯本机表现 / 日志，各机可以不同。所有模块都受 <see cref="Enabled" /> 这个主开关约束。
/// </remarks>
public sealed class PvpSettings
{
    /// <summary>主开关：关掉后本 mod 所有模块都不工作。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>入口模块：生成「对决邀请」。（⚠ 两端一致）</summary>
    public bool ModuleInvitation { get; set; } = true;

    /// <summary>额外幕模块：三幕打完进入「PVP 决斗」幕。（⚠ 两端一致）</summary>
    public bool ModuleExtraAct { get; set; } = true;

    /// <summary>调试入口：本局第一个问号房直接换成对决邀请（正式入口是额外幕）。（⚠ 两端一致）</summary>
    public bool ModuleDebugEntry { get; set; }

    /// <summary>互操作模块：进决斗前解绑 together 的共享。（⚠ 会影响共享状态）</summary>
    public bool ModuleTogetherUnbind { get; set; } = true;

    /// <summary>表现模块：决斗里的回合横幅文案（纯本机）。</summary>
    public bool ModuleBanners { get; set; } = true;

    /// <summary>表现模块：决斗限制小抄（右上角显示用量 / 上限 / 血量差，纯本机）。</summary>
    public bool ModuleLimitHud { get; set; } = true;

    /// <summary>诊断模块：只打一次的详细日志 + 战斗启动异常打印（纯本机）。</summary>
    public bool ModuleDiagnostics { get; set; } = true;

    // ==================== 分离模式（C2，实验）====================

    /// <summary>分离模式（C2 实验）：把联机链路从「一局」里剥出来。（⚠ 实验功能）</summary>
    public bool ModuleSplitMode { get; set; }

    /// <summary>分离模式下的保活：退出对局时不主动断开联机连接。（⚠ 实验功能，两端都要开）</summary>
    public bool ModuleSplitKeepAlive { get; set; }

    /// <summary>
    /// 分离模式的坐标同步（Presence 通道）。（⚠ 两端一致）
    /// </summary>
    /// <remarks>
    /// 默认开：它是分离模式的核心内容，且整条链路受 <see cref="ModuleSplitMode" /> 约束（那个默认关），
    /// 老配置文件里没有这个键时也能直接生效，省得"总开关开了却什么都没发生"。
    /// </remarks>
    public bool ModuleSplitPresence { get; set; } = true;

    /// <summary>分离模式的地图标记（在地图上画出对端）。</summary>
    public bool ModuleSplitIcon { get; set; } = true;

    /// <summary>
    /// 分离期入口：进联机局后各自切到自己的单机局（同种子）。（⚠ 实验功能，两端都要开）
    /// </summary>
    public bool ModuleSplitSoloRun { get; set; }

    /// <summary>分离模式的相遇：同节点时交换玩家快照（供合流合成 PVP 局用）。（⚠ 两端一致）</summary>
    public bool ModuleSplitMeeting { get; set; } = true;

    /// <summary>分离模式的状态行（左上角一行小字，纯本机观测）。</summary>
    public bool ModuleSplitStatusHud { get; set; } = true;

    /// <summary>回合上限：打满这么多回合还没分出胜负，就按剩余血量比例判定。</summary>
    public int MaxRounds { get; set; } = 30;

    // ==================== 平衡（决斗专用规则，都在平衡模块下）====================

    /// <summary>平衡模块：单回合出牌上限、同卡上限、额外回合上限、后手补偿。（⚠ 两端一致）</summary>
    public bool ModuleBalance { get; set; } = true;

    /// <summary>单回合出牌上限：达到就自动结束你的回合。0 = 不限制。</summary>
    public int MaxCardsPerTurn { get; set; } = 15;

    /// <summary>同一张牌每回合最多打出几次，超过就打不出去。0 = 不限制。</summary>
    public int MaxSameCardPerTurn { get; set; } = 3;

    /// <summary>同一玩家连续额外回合上限，超过就不再给额外回合。0 = 不限制。</summary>
    public int ExtraTurnLimit { get; set; } = 1;

    /// <summary>后手第一次回合的额外能量。</summary>
    public int SecondPlayerEnergy { get; set; } = 1;

    /// <summary>后手第一次回合的开局格挡。</summary>
    public int SecondPlayerBlock { get; set; } = 5;

    /// <summary>后手第一次回合的额外抽牌。</summary>
    public int SecondPlayerCards { get; set; }
}
