using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace OPERATOR.Common.Settings
{
    /// <summary>
    /// One registered plugin in the shared settings menu: its display name,
    /// its BepInEx <see cref="ConfigFile"/> (auto-rendered), and an optional
    /// custom IMGUI draw block for controls that do not map to a simple widget.
    /// </summary>
    public sealed class ModSettingsEntry
    {
        public string DisplayName { get; }
        public ConfigFile Config { get; }
        public Action CustomDraw { get; }

        public ModSettingsEntry(string displayName, ConfigFile config, Action customDraw)
        {
            DisplayName = displayName;
            Config = config;
            CustomDraw = customDraw;
        }
    }

    /// <summary>
    /// Static registry + lazy IL2CPP bootstrap for the shared OPERATOR settings menu.
    /// Plugins call <see cref="Register"/> from their <c>Load()</c>.
    /// </summary>
    public static class ModSettings
    {
        private static readonly List<ModSettingsEntry> _entries = new List<ModSettingsEntry>();
        private static bool _typeInjected;
        private static GameObject _host;

        private static ConfigFile _commonConfig;
        private static ConfigEntry<KeyCode> _toggleKey;

        /// <summary>The registered plugin entries the menu renders.</summary>
        public static IReadOnlyList<ModSettingsEntry> Entries => _entries;

        /// <summary>The global hotkey that toggles the settings menu (default F1).</summary>
        public static ConfigEntry<KeyCode> ToggleKey => _toggleKey;

        /// <summary>
        /// Register a plugin with the shared settings menu and lazily bootstrap
        /// the menu host on first call. Pass the plugin's BepInEx
        /// <c>this.Config</c>. <paramref name="customDraw"/> is drawn under the
        /// auto-rendered widgets for that plugin.
        /// </summary>
        public static void Register(string displayName, ConfigFile config, Action customDraw = null)
        {
            EnsureBootstrapped();
            _entries.Add(new ModSettingsEntry(displayName, config, customDraw));
        }

        private static void EnsureBootstrapped()
        {
            EnsureCommonConfig();

            if (_typeInjected && _host != null) return;

            if (!_typeInjected)
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModSettingsMenu>();
                _typeInjected = true;
            }

            if (_host == null)
            {
                _host = new GameObject("OPERATOR_ModSettings");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.AddComponent<ModSettingsMenu>();
            }
        }

        private static void EnsureCommonConfig()
        {
            if (_commonConfig != null) return;

            var path = Path.Combine(Paths.ConfigPath, "com.operator.common.cfg");
            _commonConfig = new ConfigFile(path, saveOnInit: true);
            _toggleKey = _commonConfig.Bind(
                "General",
                "ToggleKey",
                KeyCode.F1,
                "Hotkey that opens/closes the shared OPERATOR settings menu.");
        }
    }
}
