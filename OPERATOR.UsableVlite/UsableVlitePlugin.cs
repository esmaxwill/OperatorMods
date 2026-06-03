using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using OPERATOR.Common.Settings;
using OPERATOR.UsableVlite;
using UnityEngine;

[BepInPlugin("com.operator.usablevlite", "Usable V-Lite", "1.0.0")]
public class UsableVlitePlugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<float> InitialDelay;
    internal static ConfigEntry<float> ScanInterval;

    internal static ConfigEntry<Color>[] JoinColors;
    internal static ConfigEntry<Color> MyCustomColor;

    // NameColorOverrides doesn't map to a single ConfigEntry, so it's edited via customDraw.
    internal static readonly Dictionary<string, ConfigEntry<int>> NameColorOverrides =
        new Dictionary<string, ConfigEntry<int>>();

    public override void Load()
    {
        Logger = Log;

        // Color isn't serializable by BepInEx out of the box; register a converter so
        // Config.Bind<Color> persists and round-trips for the palette entries.
        if (!TomlTypeConverter.CanConvert(typeof(Color)))
        {
            TomlTypeConverter.AddConverter(typeof(Color), new TypeConverter
            {
                ConvertToString = (obj, type) =>
                {
                    // InvariantCulture both ways so the space-delimited form round-trips on
                    // comma-decimal locales (de-DE, fr-FR) instead of writing "0,7" and mis-splitting.
                    var c = (Color)obj;
                    return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}", c.r, c.g, c.b, c.a);
                },
                ConvertToObject = (str, type) =>
                {
                    var parts = str.Split(' ');
                    return new Color(
                        float.Parse(parts[0], CultureInfo.InvariantCulture),
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        parts.Length > 3 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 1f);
                }
            });
        }

        InitialDelay = Config.Bind("Scan", "InitialDelay", 2f,
            new ConfigDescription("Delay (seconds) before the first light scan after a player spawns.",
                new AcceptableValueRange<float>(0f, 30f)));
        ScanInterval = Config.Bind("Scan", "ScanInterval", 20f,
            new ConfigDescription("How often (seconds) to re-scan for players to attach lights to.",
                new AcceptableValueRange<float>(1f, 120f)));

        var defaults = new[]
        {
            Color.red,
            Color.yellow,
            Color.green,
            Color.blue,
            new Color(0.7f, 0f, 0.85f),  // purple
            new Color(1f, 0.55f, 0f),    // orange
        };

        JoinColors = new ConfigEntry<Color>[defaults.Length];
        for (int i = 0; i < defaults.Length; i++)
        {
            JoinColors[i] = Config.Bind("Palette", "Color" + i, defaults[i],
                "Join-index light color #" + i + ".");
        }

        MyCustomColor = Config.Bind("Palette", "MyCustomColor", Color.white,
            "My custom color.");

        // Per-name overrides: store the palette index used for that player.
        NameColorOverrides["Curufin"] = Config.Bind("NameOverrides", "Curufin", 5,
            "Palette index used for player 'Curufin'.");
        NameColorOverrides["ozzeh"] = Config.Bind("NameOverrides", "ozzeh", 4,
            "Palette index used for player 'ozzeh'.");

        Logger.LogInfo("UsableVlite loaded.");
        ClassInjector.RegisterTypeInIl2Cpp<PartyLight>();
        ModSettings.Register("Usable V-Lite", Config, DrawSettings);

        AddComponent<UsableVliteBehaviour>();
    }

    private static void DrawSettings()
    {
        GUILayout.Label("Name color overrides (palette index 0-" + (JoinColors.Length - 1) + "):");
        foreach (var kv in NameColorOverrides)
        {
            var entry = kv.Value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(kv.Key, GUILayout.Width(120f));
            int idx = entry.Value;
            float v = GUILayout.HorizontalSlider(idx, 0f, JoinColors.Length - 1, GUILayout.Width(160f));
            int newIdx = Mathf.Clamp(Mathf.RoundToInt(v), 0, JoinColors.Length - 1);
            GUILayout.Label(newIdx.ToString(), GUILayout.Width(30f));
            if (newIdx != idx)
                entry.Value = newIdx;
            GUILayout.EndHorizontal();
        }
    }
}
