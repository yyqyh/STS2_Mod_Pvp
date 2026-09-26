using HarmonyLib;

using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using PvpDuel.Core.Duel;

namespace PvpDuel.Core.Entry;

/// <summary>调试入口：把本局第一个问号房变成决斗事件（正式入口是额外幕）。</summary>
/// <remarks>
/// 本体决定「问号房变成什么」只有一处：<c>RunManager.RollRoomTypeFor</c> 里
/// <c>MapPointType.Unknown =&gt; State.Odds.UnknownMapPoint.Roll(...)</c>，改它的返回值就能 100% 控制。
/// 要求本局正好两人：单人 / 三人局把这个问号换成对决邀请，只会点一下「接受」然后什么都不发生。
/// </remarks>
[HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
internal static class DuelFirstUnknownRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunManager __instance, MapPointType __0, ref RoomType __result)
    {
        if (!ModuleSwitches.DebugEntry
            || !DuelConfig.IsTwoPlayerRun
            || __0 != MapPointType.Unknown)
        {
            return;
        }

        // RunManager.State 不是公开属性，外部只能走这个访问器。
        if (!DuelRoomPlan.TryTakeFirstUnknown(__instance.DebugOnlyGetState()))
        {
            return;
        }

        ModInfo.Info("本局第一个问号房 → 决斗事件");
        __result = RoomType.Event;
    }
}

/// <summary>让那个问号开出的是我们的「对决邀请」，而不是从事件池随机抽一个。</summary>
/// <remarks>
/// <c>CreateRoom</c> 在 Event 分支里走的是 <c>State.Act.PullNextEvent(State)</c>（随机抽），
/// 所以要在它返回之后把结果换掉。EventRoom 要的是 canonical（不可变）模型，给 Mutable 副本会被 AssertCanonical 拦下。
/// </remarks>
[HarmonyPatch(typeof(RunManager), "CreateRoom")]
internal static class DuelEventRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(RoomType __0, ref AbstractRoom __result)
    {
        if (!ModuleSwitches.DebugEntry || __0 != RoomType.Event || !DuelRoomPlan.ConsumePendingDuel())
        {
            return;
        }

        ModInfo.Info("用「对决邀请」替换掉随机事件");
        __result = new EventRoom(ModelDb.Event<DuelInvitationEvent>());
    }
}

/// <summary>吞掉决斗事件选项在「角色变量注入」那一步抛出的异常。</summary>
/// <remarks>
/// 本体 EventOption 构造时会调 AddLocVars：<c>eventModel.Owner?.Character.AddDetailsTo(Description)</c> 在我们的
/// 路径上会 NRE（Character 为 null）。关键点是 Description 在传参时已求值（异常发生时文案已就绪），
/// 后面那句只是往里面塞一个 {IsMultiplayer} 变量，我们的文案不用它，所以吞掉即可。
/// 只吞我方事件的异常，其它事件、其它异常一律原样抛回。
/// </remarks>
[HarmonyPatch(typeof(EventOption), "AddLocVars")]
internal static class DuelOptionLocVarsPatch
{
    [HarmonyFinalizer]
    private static Exception? Finalizer(EventModel __0, Exception? __exception)
    {
        if (__exception is null || __0 is not DuelInvitationEvent)
        {
            return __exception;
        }

        ModInfo.Warn(
            "已吞掉决斗事件选项的角色变量注入异常（文案已就绪，不影响显示）："
            + $"{__exception.GetType().Name}: {__exception.Message}");

        return null;
    }
}

/// <summary>记住「这一局的第一个问号已经用掉了」（换一局自动重置）。</summary>
internal static class DuelRoomPlan
{
    private static IRunState? _run;
    private static bool _used;
    private static bool _pending;

    public static bool TryTakeFirstUnknown(IRunState? runState)
    {
        if (!ReferenceEquals(_run, runState))
        {
            _run = runState;
            _used = false;
            _pending = false;
        }

        if (_used)
        {
            return false;
        }

        _used = true;
        _pending = true;
        return true;
    }

    /// <summary>取出「刚才那个问号是我们的」这个标记（只生效一次）。</summary>
    public static bool ConsumePendingDuel()
    {
        if (!_pending)
        {
            return false;
        }

        _pending = false;
        return true;
    }
}
