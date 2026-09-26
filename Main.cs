using System.Reflection;

using HarmonyLib;

using MegaCrit.Sts2.Core.Modding;
using PvpDuel.Core;
using PvpDuel.Core.Settings;
using PvpDuel.Core.SplitMode;

namespace PvpDuel;

/// <summary>「PVP 决斗」入口：只负责装配（初始化设置、逐类装补丁）。</summary>
/// <remarks>
/// 本文件<b>不含任何业务逻辑</b>。模块划分与依赖方向（自上而下单向，下层不引用上层）：
/// <list type="number">
/// <item><description>Api/ —— 对外接口（别的 mod 用，不参与本 mod 自身逻辑）。</description></item>
/// <item><description>各模块 <c>Patches/</c> —— 薄适配层：只判断「本体走到这一步该通知谁」，不含业务逻辑。</description></item>
/// <item><description>Duel/ Act/ Entry/ —— 功能模块：决斗本体、额外幕、入口事件（各自一个模块开关）。</description></item>
/// <item><description>Settings/ Interop/ Diagnostics/ —— 基础模块：设置、together 接口存根、日志与诊断。</description></item>
/// </list>
/// 命名空间跟着模块走：<c>PvpDuel</c>（入口）/ <c>PvpDuel.Core</c>（基础设施）/ <c>PvpDuel.Core.Duel</c>、
/// <c>.Act</c>、<c>.Entry</c>、<c>.Settings</c> 等，模块内的子目录只做文件分组。详见 <c>docs/ARCHITECTURE.md</c>。
/// </remarks>
[ModInitializer(nameof(Initialize))]
public static class Main
{
    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var harmony = new Harmony(ModInfo.ModId);

        // 设置要在任何「读设置」的代码之前就位。
        PvpSettingsStore.Initialize();
        PvpModSettingsPage.Register();
        SplitStatusHud.EnsureRunningDeferred();

        // 分离模式的坐标同步：模块开着就先把通道注册好（订阅要早，免得对面先发我们收不到）。
        if (ModuleSwitches.SplitPresence)
        {
            SplitPresenceChannel.Initialize();
        }

        var applied = 0;
        var failed = 0;

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
            {
                continue;
            }

            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                ModInfo.Error($"补丁类 {type.FullName} 应用失败：{Describe(ex)}");
            }
        }

        ModInfo.Info(
            $"initialized v{ModInfo.Version}; 模块：{ModuleSwitches.Describe()}; "
            + $"patch classes applied={applied} failed={failed}; "
            + $"Harmony patched {harmony.GetPatchedMethods().Count()} method(s).");
    }

    private static string Describe(Exception ex)
    {
        var parts = new List<string>();

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join("  ←  ", parts);
    }
}
