using STS2RitsuLib;
using STS2RitsuLib.Utils.Persistence;

namespace PvpDuel.Core.Settings;

/// <summary>设置的持久化入口（RitsuLib 数据存储；注册必须在 BeginModDataRegistration 作用域里且只做一次）。</summary>
internal static class PvpSettingsStore
{
    internal const string DataKey = "settings";

    private const string FileName = "pvp_duel_settings.json";

    private static bool _initialized;

    /// <summary>当前设置（每次现取，设置界面一改立刻生效）。</summary>
    public static PvpSettings Current
    {
        get
        {
            Initialize();

            return RitsuLibFramework.GetDataStore(ModInfo.ModId).Get<PvpSettings>(DataKey)
                   ?? new PvpSettings();
        }
    }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        using (RitsuLibFramework.BeginModDataRegistration(ModInfo.ModId, false))
        {
            RitsuLibFramework.GetDataStore(ModInfo.ModId).Register(
                DataKey,
                FileName,
                SaveScope.Global,
                defaultFactory: () => new PvpSettings(),
                autoCreateIfMissing: true);
        }

        RitsuLibFramework.GetDataStore(ModInfo.ModId).InitializeGlobal();
        _initialized = true;
    }
}
