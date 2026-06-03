using System;
using BepInEx.Configuration;
using UnityEngine;


namespace OPERATOR.Common.Settings
{
    /// <summary>
    /// IL2CPP-injectable MonoBehaviour that hosts the shared OPERATOR settings
    /// window. Toggled by <see cref="ModSettings.ToggleKey"/>; auto-renders each
    /// registered plugin's <see cref="ConfigFile"/> grouped by section.
    /// <para>
    /// This type is injected into IL2CPP, so it ONLY declares Unity message
    /// methods and <see cref="DrawWindow"/> (all with IL2CPP-marshalable
    /// signatures). All per-entry drawing — whose helpers take managed BepInEx
    /// types — lives in <see cref="SettingsRenderer"/>, which is never injected,
    /// to avoid Il2CppInterop "unsupported parameter" warnings.
    /// </para>
    /// </summary>
    public class ModSettingsMenu : MonoBehaviour
    {
        public ModSettingsMenu(IntPtr ptr) : base(ptr) { }

        private bool _open;
        private CursorScope _cursorScope;
        private int _selected;
        private Vector2 _listScroll;
        private Vector2 _bodyScroll;
        private Rect _window = new Rect(80f, 80f, 720f, 520f);

        private const int WindowId = 0x0FA17E5;

        private void Update()
        {
            var toggle = ModSettings.ToggleKey;
            if (toggle != null && Input.GetKeyDown(toggle.Value))
            {
                _open = !_open;
                if (_open)
                {
                    _cursorScope = new CursorScope();
                    BeginEditing();
                }
                else
                    Close();
            }
        }

        private void LateUpdate()
        {
            if (!_open) return;
            // Win over the game's CursorLock each frame so the menu stays clickable.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnGUI()
        {
            if (!_open) return;
            _window = GUILayout.Window(WindowId, _window, (GUI.WindowFunction)DrawWindow, "OPERATOR Mods Settings");
        }

        private void DrawWindow(int id)
        {
            var entries = ModSettings.Entries;

            GUILayout.BeginHorizontal();

            // Left column: plugin list.
            GUILayout.BeginVertical(GUILayout.Width(180f));
            _listScroll = GUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < entries.Count; i++)
            {
                bool sel = i == _selected;
                bool now = GUILayout.Toggle(sel, entries[i].DisplayName, "Button");
                if (now && !sel) _selected = i;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            // Right pane: selected plugin's settings (delegated to the renderer).
            GUILayout.BeginVertical();
            _bodyScroll = GUILayout.BeginScrollView(_bodyScroll);
            if (_selected >= 0 && _selected < entries.Count)
                SettingsRenderer.DrawEntry(entries[_selected]);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            if (GUILayout.Button("Close")) Close();

            GUI.DragWindow(new Rect(0f, 0f, 100000f, 24f));
        }

        // Defer disk writes while the menu is open. A slider/color drag assigns BoxedValue many
        // times per second, and each assign would otherwise trigger ConfigFile.Save() — a full
        // synchronous rewrite of the whole .cfg. With SaveOnConfigSet off we save once, on Close().
        private static void BeginEditing()
        {
            var entries = ModSettings.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Config != null) entries[i].Config.SaveOnConfigSet = false;
        }

        private void Close()
        {
            _open = false;
            _cursorScope?.Restore();
            _cursorScope = null;
            SettingsRenderer.Reset();
            FlushSaves();
        }

        // Safety net: if the game quits while the menu is still open, deferred edits would otherwise
        // be lost (SaveOnConfigSet is off while open). Flush them here too.
        private void OnApplicationQuit()
        {
            if (_open) FlushSaves();
        }

        // Persist once, then restore immediate-save for any config changes made outside the menu.
        private static void FlushSaves()
        {
            var entries = ModSettings.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var cfg = entries[i].Config;
                if (cfg == null) continue;
                cfg.SaveOnConfigSet = true;
                try { cfg.Save(); }
                catch (Exception e) { Debug.LogWarning("[ModSettings] config save failed: " + e.Message); }
            }
        }
    }
}
