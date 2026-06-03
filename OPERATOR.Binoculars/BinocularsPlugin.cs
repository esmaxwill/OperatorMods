using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using OPERATOR.Common.Settings;

[BepInPlugin("com.operator.binoculars", "Binoculars", "1.0.0")]
public class BinocularsPlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<KeyCode> ZoomInKey;
    internal static ConfigEntry<KeyCode> ZoomInKey2;
    internal static ConfigEntry<KeyCode> ZoomOutKey;
    internal static ConfigEntry<KeyCode> ZoomOutKey2;

    internal static ConfigEntry<float> FovMin;
    internal static ConfigEntry<float> FovMax;
    internal static ConfigEntry<float> FovDefault;
    internal static ConfigEntry<float> FovStep;
    internal static ConfigEntry<float> MaxRange;

    public override void Load()
    {
        Logger = Log;

        ToggleKey   = Config.Bind("Keys", "ToggleKey",   KeyCode.F4,          "Toggle the binoculars on/off.");
        ZoomInKey   = Config.Bind("Keys", "ZoomInKey",   KeyCode.Equals,      "Zoom in (decrease FOV).");
        ZoomInKey2  = Config.Bind("Keys", "ZoomInKey2",  KeyCode.KeypadPlus,  "Alternate zoom in key.");
        ZoomOutKey  = Config.Bind("Keys", "ZoomOutKey",  KeyCode.Minus,       "Zoom out (increase FOV).");
        ZoomOutKey2 = Config.Bind("Keys", "ZoomOutKey2", KeyCode.KeypadMinus, "Alternate zoom out key.");

        FovMin     = Config.Bind("Zoom", "FovMin",     5f,    new ConfigDescription("Minimum field of view (most zoom).",  new AcceptableValueRange<float>(1f, 5f)));
        FovMax     = Config.Bind("Zoom", "FovMax",     40f,   new ConfigDescription("Maximum field of view (least zoom).", new AcceptableValueRange<float>(1f, 40f)));
        FovDefault = Config.Bind("Zoom", "FovDefault", 20f,   new ConfigDescription("Field of view on activation.",        new AcceptableValueRange<float>(1f, 40f)));
        FovStep    = Config.Bind("Zoom", "FovStep",    2.5f,  new ConfigDescription("Field of view change per zoom step.", new AcceptableValueRange<float>(0.5f, 20f)));
        MaxRange   = Config.Bind("Rangefinder", "MaxRange", 300f, new ConfigDescription("Maximum rangefinder distance in metres.", new AcceptableValueRange<float>(100f, 300f)));

        Logger.LogInfo("Binoculars loaded.");
        ModSettings.Register("Binoculars", Config);
        AddComponent<BinocularsBehaviour>();
    }
}
