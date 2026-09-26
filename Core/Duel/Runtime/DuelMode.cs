using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace PvpDuel.Core.Duel;

/// <summary>把战斗改造成决斗：清掉怪物、补齐战斗录像快照。</summary>
/// <remarks>
/// <para>
/// <b>两人都留在我方侧，对手只是看起来在对面</b>：早先试过把 <c>Players[1]</c> 的
/// <c>Creature.Side</c> 改成敌方，代价是同时踩中本体五处「怪物专属」路径
/// （<c>TakeTurn</c> / <c>AfterAddedToRoom</c> / <c>RollMove</c> / <c>PrepareForNextTurn</c> / <c>PerformIntent</c>）
/// 并且要接管整个敌方回合。现在只改两处「看法」——立绘位置与朝向（<c>Patches/LayoutPatches.cs</c>）、
/// 敌我视角（<c>Patches/CombatPatches.cs</c> 与 <c>Patches/TargetPatches.cs</c>）。
/// </para>
/// <para>
/// <b>对手是谁必须两端算出同一个答案</b>：固定用 <c>Players[1]</c>（大厅顺序靠后的那位），
/// 不能用本机视角判断，否则主机和客户端会当场分叉。
/// </para>
/// </remarks>
internal static class DuelMode
{
    /// <summary>幂等地对齐一场战斗的决斗布置（由 AddPlayer 补丁在每位玩家加入后调用）。</summary>
    public static void EnsureApplied(CombatState? state)
    {
        if (state is null || !DuelRules.CanRunDuel(state))
        {
            return;
        }

        DuelSession.Begin(state);

        if (!DuelSession.InDuel)
        {
            return;
        }

        EnsureReplaySnapshot();
        RemoveMonsters(state);
    }

    /// <summary>补上战斗录像的初始快照。</summary>
    /// <remarks>
    /// 本体只在「走地图点」和「按房间类型直接进房」两条路上记它，事件里开打（EnterCombatWithoutExitingEvent）不记。
    /// 快照为空时 CombatReplayWriter 的每个钩子都会抛 RecordInitialState must be called first，
    /// 回合开始的第一个 checksum 正好踩中它，整条回合链当场死掉（表现：战斗卡住不动）。
    /// </remarks>
    private static void EnsureReplaySnapshot()
    {
        try
        {
            var run = RunManager.Instance;
            var writer = run?.CombatReplayWriter;

            if (run is null || writer is null || !writer.IsEnabled || writer.IsRecordingReplay)
            {
                return;
            }

            writer.RecordInitialState(run.ToSave(null));

            ModInfo.LogOnce("决斗：补上战斗录像的初始快照");
        }
        catch (Exception ex)
        {
            ModInfo.Warn($"补战斗录像快照失败（不影响战斗）：{ex.Message}");
        }
    }

    /// <summary>纯玩家对决：把敌方侧的怪物全部摘掉。</summary>
    private static void RemoveMonsters(CombatState state)
    {
        foreach (var creature in state.Enemies.ToList())
        {
            if (creature.IsPlayer)
            {
                continue;
            }

            try
            {
                state.RemoveCreature(creature, unattach: true);
                ModInfo.Info($"移除怪物：{creature.Monster?.Id.Entry ?? "?"}");
            }
            catch (Exception ex)
            {
                ModInfo.Warn($"移除怪物失败：{ex.Message}");
            }
        }
    }
}
