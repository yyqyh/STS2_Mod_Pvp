using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace PvpDuel.Core.SplitMode;

/// <summary>
/// 分离期入口：在<b>大厅按下「开始」的那一刻</b>拦下来，各自开始自己的单机局（同种子）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么拦在大厅而不是进局之后再切：先切要等联机局真的开起来（两人已经共享跑了几步、可能进了战斗），
/// 再 <c>CleanUp</c> 会把这一段丢掉，还要和一个"活着的对局"抢状态；拦在大厅则从头就是干净的——
/// 联机局压根没被创建，<c>RunManager.State</c> 还是 null，起单机局不需要任何清场。
/// </para>
/// <para>
/// 连接不用重连：调用方紧接着就会 <c>CleanUpLobby(disconnectSession: false)</c>（本体自己的行为），
/// 只解注册大厅、不断开。我们要做的是把那条传输<b>提前抓进 <see cref="SplitLink" /></b>——
/// 因为接下来本机跑的是单机局，本体不会再有人泵它了（详见 SplitLink 的注释）。
/// </para>
/// <para>已知取舍：单机局用的是<b>新玩家</b>（角色/进阶照搬，卡组与遗物是初始的），不继承联机局的进度。</para>
/// </remarks>
internal static class SplitSoloRun
{
    /// <summary>能不能拦（开关开着 + 大厅信息齐全）。不满足就让本体照常开联机局。</summary>
    public static bool CanIntercept(StartRunLobby? lobby)
    {
        // 分离模式独立于「相遇决斗」：这里只问分离模式自己。
        // 分离模式关掉 → 不拦 → 本体照常开联机共享局 → 老玩法（额外幕 + 「对决邀请」事件 → PVP）。
        return ModuleSwitches.SplitSoloRun
               && NGame.Instance is not null
               && lobby is not null
               && lobby.LocalPlayer.character is not null;
    }

    /// <summary>代替本体开始联机局：本机开一局自己的单机局，并把大厅那条传输接管下来。</summary>
    public static async Task<RunState> StartInsteadOfMultiplayer(
        StartRunLobby lobby,
        IReadOnlyList<ActModel> acts,
        IReadOnlyList<ModifierModel> modifiers,
        string seed,
        int ascensionLevel)
    {
        var game = NGame.Instance!;
        var character = lobby.LocalPlayer.character;

        // 分离 = 各自为战：先把 together 的共生体解掉。
        // 否则它还认为两人"共用身体"，会把金币/能力/卡组往对面推（日志里那些"金币同步"就是它干的），
        // 而我们马上要跑的是两台各自独立的单机局——两边的镜像互相打架。
        if (ModuleSwitches.TogetherUnbind)
        {
            Interop.TogetherInterop.Unbind("split_solo");
        }

        // 大厅这条传输就是分离期的生命线：本体马上不会再泵它了，先抓住、守住。
        SplitLink.Capture(lobby.NetService);
        SplitLink.EnsurePump();
        SplitStatusHud.EnsureRunning();
        SplitPresenceChannel.Initialize();
        SplitLink.Arm();

        ModInfo.Info(
            $"[SplitMode] 大厅拦截：不开始联机局，各自开单机局"
            + $"（角色={character.Id.Entry} 种子={seed} 进阶={ascensionLevel} 幕数={acts.Count}）");

        var runState = await game.StartNewSingleplayerRun(
            character,
            shouldSave: false,
            acts,
            modifiers,
            seed,
            GameMode.Standard,
            ascensionLevel);

        ModInfo.Info("[SplitMode] 大厅拦截：自己的单机局已开始（连接留着，坐标照发）");

        return runState;
    }
}
