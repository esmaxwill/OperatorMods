using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using OPERATOR.Common;
using OPERATOR.Common.Settings;

[BepInPlugin("com.operator.functionalweaponmods", "Functional Weapon Mods", "0.1.0")]
public class FunctionalWeaponModsPlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<bool>  Enabled;
    internal static ConfigEntry<float> WeightFactor;
    internal static ConfigEntry<float> ErgoFactor;
    internal static ConfigEntry<float> RecoilFactor;

    public override void Load()
    {
        Logger = Log;

        Enabled = Config.Bind("General", "Enabled", true,
            "Apply attachment Weight/Ergonomics/Recoil aggregation to your own weapons.");
        WeightFactor = Config.Bind("Tuning", "WeightFactor", 1f,
            new ConfigDescription("Multiplier on summed attachment Weight.", new AcceptableValueRange<float>(0f, 5f)));
        ErgoFactor = Config.Bind("Tuning", "ErgoFactor", 1f,
            new ConfigDescription("Multiplier on summed attachment Ergonomics.", new AcceptableValueRange<float>(0f, 5f)));
        RecoilFactor = Config.Bind("Tuning", "RecoilFactor", 1f,
            new ConfigDescription("Multiplier on summed attachment Recoil (exposed for the recoil hook).", new AcceptableValueRange<float>(0f, 5f)));

        ModSettings.Register("Functional Weapon Mods", Config);

        // Reset the per-weapon registry when the local player unloads (avoids cross-session growth).
        PlayerLifecycle.OnPlayerUnloaded += FunctionalWeaponMods.ClearAll;
        PlayerLifecycleWatcher.EnsureRunning();   // start the watcher that fires OnPlayerUnloaded

        new Harmony("com.operator.functionalweaponmods").PatchAll();
        Logger.LogInfo("FunctionalWeaponMods loaded.");
    }
}
