using HarmonyLib;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace PvpDuel.Core.Duel;

/// <summary>战斗<b>状态级</b>补丁：谁进了战斗、怪物进不进、谁倒下算输、战斗何时结束、谁算「可选敌人」。</summary>
/// <remarks>
/// 都是「本体走到这一步，决斗要改口」的薄适配（不含业务逻辑），判定本身在 <see cref="DuelRules" /> / <see cref="DuelSession" />。
/// 站位、朝向这类纯表现的在 <c>LayoutPatches</c>。
/// </remarks>

/// <summary>战斗初始化：每加进来一名玩家就对齐一次决斗布置（幂等）。</summary>
[HarmonyPatch(typeof(CombatState), nameof(CombatState.AddPlayer))]
internal static class DuelArmOnPlayerAddedPatch
{
    [HarmonyPostfix]
    private static void Postfix(CombatState __instance)
    {
        DuelMode.EnsureApplied(__instance);
    }
}

/// <summary>纯玩家对决：怪物一律不进战斗。</summary>
/// <remarks>AddCreature 是怪物进入战斗的唯一入口，在这里拦下比「先放进来再摘掉」干净（后者会牵动意图、AI 一堆初始化）。</remarks>
[HarmonyPatch(typeof(CombatState), nameof(CombatState.AddCreature))]
internal static class DuelStripMonstersPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CombatState __instance, Creature __0)
    {
        // 只拦「属于这一场遭遇的怪」：玩家自己的宠物 / 召唤物要放行 ——
        // 它们随后走的是 CombatManager.AddCreature，而那个方法要求「CombatState 里已经有这个 creature」，
        // 被我们拦掉就会在 BeforeCombatStart 里抛 InvalidOperationException，
        // 整个回合循环直接死掉（表现：战斗卡死、log 里 "turn loop died … stuck"）。
        // 实例：缚魂瓶（BoundPhylactery）开战时召唤 OSTY。
        if (__0 is null
            || __0.IsPlayer
            || __0.PetOwner is not null
            || !DuelSession.InDuel
            || !DuelRules.CanRunDuel(__instance))
        {
            return true;
        }

        ModInfo.Info($"决斗模式：拦下怪物 {__0.Monster?.Id.Entry ?? "?"} 不进战斗");
        return false;
    }
}

/// <summary>胜负：决斗里任意一方倒下就该结束。</summary>
/// <remarks>
/// 要接管的是「我方那位倒下」：本体的失败判据是「所有玩家都死了」，而对手还活着，这个条件永远不成立，战斗会卡住。
/// </remarks>
[HarmonyPatch(
    typeof(CreatureCmd),
    nameof(CreatureCmd.Kill),
    new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) })]
internal static class DuelPlayerDownPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool force, out bool __state)
    {
        // 主动放弃这一局（force）不算决斗分出胜负，走本体原本的流程即可。
        __state = force;
    }

    [HarmonyPostfix]
    private static void Postfix(IReadOnlyCollection<Creature> __0, bool __state)
    {
        if (__state || __0 is null || !DuelSession.InDuel)
        {
            return;
        }

        var deadPlayer = __0.FirstOrDefault(creature => creature.IsPlayer && creature.IsDead);
        if (deadPlayer?.CombatState is not { } state || !DuelRules.CanRunDuel(state))
        {
            return;
        }

        // 两人都在我方侧，所以这里判断「倒下的是不是本机那位」。
        var me = LocalContext.GetMe(state)?.Creature;

        DuelSession.Finish(victory: !ReferenceEquals(deadPlayer, me));
    }
}

/// <summary>决斗里的战斗不能因为「敌方侧没人」就结束。</summary>
/// <remarks>
/// 本体 IsCombatEnding 的判据是「敌方侧还有活着的 PrimaryEnemy」，而决斗一开打敌方侧就是空的 ——
/// 不压住这个结果，战斗刚开始就会被判成敌人全灭、直接弹结算。
/// 压胜负判定是很重的动作，条件必须带「正好两人」，否则单人 / 三人局那场战斗永远结束不了。
/// </remarks>
[HarmonyPatch(typeof(CombatManager), "IsCombatEnding")]
internal static class DuelKeepCombatAlivePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        if (!__result || !DuelSession.InDuel || !DuelRules.CanRunDuel(DuelSession.Combat))
        {
            return;
        }

        ModInfo.LogOnce("决斗：敌方侧没有怪物，但不结束战斗（避免开局即判定胜利）");
        __result = false;
    }
}

/// <summary>把「敌人」从共享一份列表改成每个玩家各有自己的视角。</summary>
/// <remarks>
/// HittableEnemies 同时被规则层（攻击牌、AOE、随机目标）和 UI 层（拖牌时的可选目标）读。
/// 决斗里敌方侧是空的，所以这里按本机玩家重算：候选 = 另一位玩家 + 他名下的召唤物。
/// （自动效果不走这里 —— 它们用 DuelRules.OpponentsOf，那份不看本机视角。）
/// 这是本地视角，不涉及跨端一致性 —— 目标选择本来就是本地 UI 行为，伤害由同步的动作结算。
/// </remarks>
[HarmonyPatch(typeof(CombatState), "get_HittableEnemies")]
internal static class DuelHittableOpponentPatch
{
    [HarmonyPostfix]
    private static void Postfix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        if (__result is null || !DuelRules.IsInTwoPlayerDuel(__instance))
        {
            return;
        }

        if (LocalContext.GetMe(__instance)?.Creature is not { } mine)
        {
            return;
        }

        var opponents = DuelRules.OpponentsOf(mine);

        if (opponents.Count == 0)
        {
            return;
        }

        var merged = new List<Creature>(__result.Count + opponents.Count);

        foreach (var creature in __result)
        {
            // 自己绝不能出现在自己的攻击候选里。
            if (!ReferenceEquals(creature, mine))
            {
                merged.Add(creature);
            }
        }

        foreach (var unit in opponents)
        {
            if (!merged.Any(creature => ReferenceEquals(creature, unit)))
            {
                merged.Add(unit);
            }
        }

        ModInfo.LogOnce(
            $"按本机视角重算可选敌人：本机={mine.Player?.NetId ?? 0} 候选={merged.Count} 个");

        __result = merged;
    }
}
