using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Rewards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using PvpDuel.Core.Act;
using PvpDuel.Core.Duel;
using PvpDuel.Core.Interop;

namespace PvpDuel.Core.Entry;

/// <summary>决斗的入口事件：「对决邀请」，挂在 <see cref="PvpDuelAct" /> 的事件池里。</summary>
/// <remarks>
/// <para>
/// 进战斗走的是<b>选项回调里的 <c>EnterCombatWithoutExitingEvent</c></b>，不是覆盖 <c>CanonicalEncounter</c>：
/// 后者会让本体走「事件战斗」那条更早的初始化路径，实测那条路上事件还没绑好 Owner，
/// <c>EventOption</c> 构造时会在 <c>CharacterModel.AddDetailsTo</c> 里 NRE。
/// </para>
/// <para>
/// 那个遭遇只是<b>占位</b>——它的怪物会被决斗逻辑在 <c>CombatState.AddCreature</c> 处全部拦下。
/// 立绘先借本体的图：RitsuLib 判存在性走 <c>ResourceLoader.Exists</c>，mod 包里的 png 在那个索引里查不到。
/// </para>
/// </remarks>
[RegisterActEvent(typeof(PvpDuelAct))]
public sealed class DuelInvitationEvent : ModEventTemplate
{
    /// <summary>联机时事件对所有玩家一致（决斗必须是两个人的事）。</summary>
    public override bool IsShared => true;

    public override EventAssetProfile AssetProfile =>
        new(InitialPortraitPath: "res://images/events/battleworn_dummy.png");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new(this, BeginDuel, InitialOptionKey("BEGIN")),
        ];
    }

    private Task BeginDuel()
    {
        // 事件挂进事件池之后任何一局都可能抽到，所以不满足「正好两人」就地收掉事件页。
        if (!ModuleSwitches.Invitation || !DuelConfig.IsTwoPlayerRun)
        {
            ModInfo.Warn($"决斗事件被触发，但本机不满足「入口模块开 + 正好两人」（入口={ModuleSwitches.Invitation}），忽略");
            SetEventFinished(PageDescription("BEGIN"));
            return Task.CompletedTask;
        }

        // 决斗是各自为战：先把可能存在的共生体关系解除，共享卡组会按奇偶分给两边。
        // together 没装 / 没配对时是空操作；这一条也单独开关（互操作模块）。
        if (ModuleSwitches.TogetherUnbind)
        {
            TogetherInterop.Unbind("duel_invitation");
        }

        DuelSession.Arm();

        // 占位遭遇：怪物会被决斗逻辑拦下，实际打的是两人轮流行动。
        EnterCombatWithoutExitingEvent(ModelDb.Encounter<CorpseSlugsWeak>(), [], shouldResumeAfterCombat: false);

        SetEventFinished(PageDescription("BEGIN"));
        return Task.CompletedTask;
    }
}
