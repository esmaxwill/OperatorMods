using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using OPERATOR.Common.Settings;

[BepInPlugin("com.operator.playerloadouts", "Player Loadouts", "0.1.0")]
public class PlayerLoadoutsPlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo("PlayerLoadouts loaded.");
        ModSettings.Register("Player Loadouts", Config);
        AddComponent<PlayerLoadoutsBehaviour>();
    }
}
