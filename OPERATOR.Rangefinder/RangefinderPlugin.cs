using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OPERATOR.Common.Settings;

[BepInPlugin("com.operator.rangefinder", "Laser Rangefinder", "1.0.0")]
public class RangefinderPlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;

    public override void Load()
    {
        Logger = Log;

        Enabled = Config.Bind("General", "Enabled", true, "Enable the laser rangefinder overlay while aiming through a scope.");

        Logger.LogInfo("Rangefinder loaded.");
        ModSettings.Register("Laser Rangefinder", Config);
        AddComponent<RangefinderBehaviour>();
    }
}
