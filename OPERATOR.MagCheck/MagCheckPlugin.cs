using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

[BepInPlugin("com.operator.magcheck", "Mag Check", "1.0.0")]
public class MagCheckPlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        ClassInjector.RegisterTypeInIl2Cpp<MagCheckBehaviour>();
        AddComponent<MagCheckBehaviour>();
        new Harmony("com.operator.magcheck").PatchAll();
        Logger.LogInfo("MagCheck loaded.");
    }
}
