using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

using PvpDuel.Core.Entry;

namespace PvpDuel.Core.Duel;

/// <summary>
/// 一场决斗的会话：标记、轮流行动这本账、回合上限、以及结束结算。
/// </summary>
/// <remarks>
/// <para>
/// 生命周期（由补丁在对应时机驱动，本类不自己去挂钩子）：
/// 事件里 <see cref="Arm" /> → 战斗初始化 <see cref="Begin" /> → 每个玩家回合开始
/// <see cref="BeginActorTurn" /> → 每个回合 <see cref="TryFinishByRoundLimit" /> →
/// 分出胜负 <see cref="Finish" />。
/// </para>
/// <para>
/// 之所以把状态集中在这里：决胜条件分散时最难查的就是"某个标记没清"。现在一场战斗的全部运行时状态
/// （是否决斗、轮到谁、谁行动过、是否已结算）只在这一个对象里，读代码时不用去追四个静态类。
/// </para>
/// </remarks>
internal static class DuelSession
{
    private static readonly HashSet<Player> Acted = [];

    private static CombatState? _begun;
    private static int _actorIndex;

    /// <summary>下一场战斗是决斗（事件里点了「接受对决」）。</summary>
    public static bool Armed { get; private set; }

    /// <summary>当前这场战斗是决斗。</summary>
    public static bool InDuel { get; private set; }

    /// <summary>当前这场决斗（供回合上限等检查用）。</summary>
    public static CombatState? Combat { get; private set; }

    /// <summary>本回合该谁行动。</summary>
    public static Player? Actor { get; private set; }

    /// <summary>这一场已经分出胜负（防重复结算）。</summary>
    public static bool IsFinished { get; private set; }

    /// <summary>事件里登记「下一场是决斗」（只对下一场有效）。</summary>
    public static void Arm()
    {
        Armed = true;
        ModInfo.Info("已登记：下一场战斗为决斗");
    }

    /// <summary>战斗初始化：消费标记，并重置这一场的账（同一场重复调用无副作用）。</summary>
    public static void Begin(CombatState state)
    {
        Combat = state;

        if (ReferenceEquals(_begun, state))
        {
            return;
        }

        _begun = state;
        _actorIndex = 0;
        Acted.Clear();
        DuelBalance.Reset();
        Actor = null;
        IsFinished = false;

        // 读档 / 重连之后 Armed 是内存态、已经丢了，但"这一场是不是决斗"能从房间上读出来：
        // 事件开战会把事件 id 记进 CombatRoom.ParentEventId，而那个字段是跟着存档走的。
        InDuel = Armed || StartedFromDuelEvent();
        Armed = false;

        ModInfo.Info(
            $"新的战斗：allies={state.Allies.Count} enemies={state.Enemies.Count} "
            + $"players={state.Players.Count} → {(InDuel ? "决斗" : "普通战斗（不做任何改造）")}");
    }

    /// <summary>这一场战斗是不是由「对决邀请」开出来的（存档 / 重连恢复时用）。</summary>
    private static bool StartedFromDuelEvent()
    {
        return RunManager.Instance.DebugOnlyGetState()?.CurrentRoom
               is CombatRoom { ParentEventId: { } parentEventId }
               && parentEventId == ModelDb.Event<DuelInvitationEvent>().Id;
    }

    /// <summary>我方回合开始：轮到下一位行动者。</summary>
    public static void BeginActorTurn(ICombatState state)
    {
        var players = state.Players;

        if (players.Count == 0)
        {
            return;
        }

        Actor = players[_actorIndex % players.Count];
        _actorIndex++;
        Acted.Add(Actor);

        ModInfo.Info($"本回合行动者：netId={Actor.NetId}");
    }

    /// <summary>回合上限：打满 N 个回合还没分胜负，就按剩余血量百分比判定。</summary>
    public static void TryFinishByRoundLimit()
    {
        if (IsFinished || !ModuleSwitches.Core)
        {
            return;
        }

        if (Combat is not { } state || state.Players.Count != 2)
        {
            return;
        }

        var maxRounds = DuelConfig.MaxRounds;

        if (state.RoundNumber < maxRounds)
        {
            return;
        }

        var victory = DuelRules.TimeoutWinnerIsLocalPlayer(state, out var mine, out var theirs);

        ModInfo.Info(
            $"达到回合上限 {maxRounds}：我方 {mine:P0} vs 对手 {theirs:P0} → "
            + (victory ? "判定胜利" : "判定失败"));

        Finish(victory);
    }

    /// <summary>结束这一局（决斗一局定胜负，直接走本体的跑局结算）。</summary>
    public static void Finish(bool victory)
    {
        if (IsFinished || !RunManager.Instance.IsInProgress || RunManager.Instance.IsCleaningUp)
        {
            return;
        }

        IsFinished = true;

        ModInfo.Info($"决斗结束：{(victory ? "我方胜利" : "我方失败")}");

        if (CombatManager.Instance.IsInProgress)
        {
            CombatManager.Instance.LoseCombat();
        }

        if (NRun.Instance is { } run)
        {
            run.RunMusicController.StopMusic();
            NAudioManager.Instance?.PlayMusic(
                victory ? "event:/temp/sfx/victory" : "event:/temp/sfx/game_over");
        }

        NRun.Instance?.ShowGameOverScreen(RunManager.Instance.OnEnded(victory));
    }
}
