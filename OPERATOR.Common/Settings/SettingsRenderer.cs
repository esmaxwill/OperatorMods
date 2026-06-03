using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace OPERATOR.Common.Settings
{
    /// <summary>
    /// Plain managed renderer for the settings window. Deliberately NOT a
    /// MonoBehaviour and never injected into IL2CPP, so its methods can take
    /// managed BepInEx types (<see cref="ConfigEntryBase"/>, <see cref="Type"/>)
    /// without Il2CppInterop emitting "unsupported parameter" warnings.
    /// <see cref="ModSettingsMenu"/> (the injected host) only keeps Unity message
    /// methods and delegates all per-entry drawing here.
    /// </summary>
    internal static class SettingsRenderer
    {
        // The entry currently capturing a key rebind (null = none). Static because
        // there is only ever one ModSettingsMenu instance.
        internal static ConfigEntryBase Capturing;

        // Cached once: Enum.GetValues(typeof(KeyCode)) allocates a ~320-element array, and the
        // key-capture fallback used to rebuild it on every OnGUI pass while capturing.
        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        internal static void Reset() => Capturing = null;

        internal static void DrawEntry(ModSettingsEntry entry)
        {
            var cfg = entry.Config;

            GUILayout.Label(entry.DisplayName, GUI.skin.box);

            if (cfg != null)
            {
                // Group config entries by section, preserving order.
                var sections = new List<string>();
                var bySection = new Dictionary<string, List<ConfigEntryBase>>();

                foreach (var def in cfg.Keys)
                {
                    ConfigEntryBase ceb = cfg[def];
                    if (ceb == null) continue;

                    string section = def.Section ?? "";
                    if (!bySection.TryGetValue(section, out var list))
                    {
                        list = new List<ConfigEntryBase>();
                        bySection[section] = list;
                        sections.Add(section);
                    }
                    list.Add(ceb);
                }

                foreach (var section in sections)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(section, GUI.skin.box);
                    foreach (var ceb in bySection[section])
                        DrawRow(ceb);
                }
            }

            // Hybrid custom draw block.
            if (entry.CustomDraw != null)
            {
                GUILayout.Space(8f);
                entry.CustomDraw();
            }
        }

        private static void DrawRow(ConfigEntryBase entry)
        {
            string label = entry.Definition.Key;
            string desc = entry.Description != null ? entry.Description.Description : null;
            Type t = entry.SettingType;

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, desc), GUILayout.Width(180f));

            if (t == typeof(bool))
            {
                bool v = (bool)entry.BoxedValue;
                bool nv = GUILayout.Toggle(v, "");
                if (nv != v) entry.BoxedValue = nv;
            }
            else if (t == typeof(KeyCode))
            {
                DrawKeybind(entry);
            }
            else if (t.IsEnum)
            {
                DrawEnum(entry, t);
            }
            else if (t == typeof(Color))
            {
                DrawColor(entry);
            }
            else if (IsNumeric(t) && TryGetRange(entry, out double min, out double max))
            {
                DrawSlider(entry, t, min, max);
            }
            else
            {
                DrawTextField(entry, t);
            }

            // Reset affordance.
            if (entry.DefaultValue != null && GUILayout.Button("Reset", GUILayout.Width(56f)))
                entry.BoxedValue = entry.DefaultValue;

            GUILayout.EndHorizontal();
        }

        private static void DrawEnum(ConfigEntryBase entry, Type t)
        {
            var values = Enum.GetValues(t);
            object cur = entry.BoxedValue;
            if (GUILayout.Button(cur.ToString()))
            {
                int idx = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if (Equals(values.GetValue(i), cur)) { idx = i; break; }
                }
                idx = (idx + 1) % values.Length;
                entry.BoxedValue = values.GetValue(idx);
            }
        }

        private static void DrawKeybind(ConfigEntryBase entry)
        {
            bool capturing = Capturing == entry;
            var cur = (KeyCode)entry.BoxedValue;
            string text = capturing ? "Press a key..." : cur.ToString();
            if (GUILayout.Button(text))
            {
                Capturing = capturing ? null : entry;
            }

            if (capturing)
            {
                // Capture the next pressed key via the current IMGUI Event,
                // falling back to polling Input for keys Event may not surface.
                Event e = Event.current;
                if (e != null && e.isKey && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
                {
                    if (e.keyCode == KeyCode.Escape)
                    {
                        Capturing = null;
                    }
                    else
                    {
                        entry.BoxedValue = e.keyCode;
                        Capturing = null;
                    }
                    e.Use();
                    return;
                }

                // Poll the cached KeyCode list once per frame (the Layout pass) rather than on every
                // OnGUI pass — each Input.GetKeyDown is an interop boundary call.
                if (e == null || e.type == EventType.Layout)
                {
                    for (int i = 0; i < AllKeyCodes.Length; i++)
                    {
                        KeyCode kc = AllKeyCodes[i];
                        if (kc == KeyCode.None) continue;
                        if (Input.GetKeyDown(kc))
                        {
                            if (kc == KeyCode.Escape)
                                Capturing = null;
                            else
                            {
                                entry.BoxedValue = kc;
                                Capturing = null;
                            }
                            break;
                        }
                    }
                }
            }
        }

        private static void DrawColor(ConfigEntryBase entry)
        {
            var c = (Color)entry.BoxedValue;
            GUILayout.BeginVertical();
            float r = LabeledSlider("R", c.r, 0f, 1f);
            float g = LabeledSlider("G", c.g, 0f, 1f);
            float b = LabeledSlider("B", c.b, 0f, 1f);
            GUILayout.EndVertical();
            var nc = new Color(r, g, b, c.a);
            if (nc != c) entry.BoxedValue = nc;
        }

        private static float LabeledSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(16f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(160f));
            GUILayout.Label(v.ToString("0.00", CultureInfo.InvariantCulture), GUILayout.Width(48f));
            GUILayout.EndHorizontal();
            return v;
        }

        private static void DrawSlider(ConfigEntryBase entry, Type t, double min, double max)
        {
            double cur = Convert.ToDouble(entry.BoxedValue, CultureInfo.InvariantCulture);
            float nv = GUILayout.HorizontalSlider((float)cur, (float)min, (float)max, GUILayout.Width(180f));
            GUILayout.Label(nv.ToString("0.##", CultureInfo.InvariantCulture), GUILayout.Width(56f));

            object converted = CoerceNumeric(t, nv);
            if (converted != null && !converted.Equals(entry.BoxedValue))
                entry.BoxedValue = converted;
        }

        private static void DrawTextField(ConfigEntryBase entry, Type t)
        {
            object cur = entry.BoxedValue;
            string s = cur != null ? cur.ToString() : "";
            string ns = GUILayout.TextField(s, GUILayout.Width(180f));
            if (ns != s)
            {
                if (t == typeof(string))
                {
                    entry.BoxedValue = ns;
                }
                else if (IsNumeric(t))
                {
                    object parsed = TryParseNumeric(t, ns);
                    if (parsed != null) entry.BoxedValue = parsed;
                }
            }
        }

        private static bool IsNumeric(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double)
                || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort)
                || t == typeof(sbyte) || t == typeof(decimal);
        }

        private static bool TryGetRange(ConfigEntryBase entry, out double min, out double max)
        {
            min = 0; max = 0;
            var acc = entry.Description != null ? entry.Description.AcceptableValues : null;
            if (acc == null) return false;

            var at = acc.GetType();
            if (!at.IsGenericType || at.GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
                return false;

            var minProp = at.GetProperty("MinValue");
            var maxProp = at.GetProperty("MaxValue");
            if (minProp == null || maxProp == null) return false;

            min = Convert.ToDouble(minProp.GetValue(acc), CultureInfo.InvariantCulture);
            max = Convert.ToDouble(maxProp.GetValue(acc), CultureInfo.InvariantCulture);
            return true;
        }

        private static object CoerceNumeric(Type t, float v)
        {
            try
            {
                if (t == typeof(int)) return Mathf.RoundToInt(v);
                if (t == typeof(long)) return (long)Math.Round(v);
                if (t == typeof(short)) return (short)Math.Round(v);
                if (t == typeof(byte)) return (byte)Math.Round(v);
                if (t == typeof(uint)) return (uint)Math.Round(v);
                if (t == typeof(ulong)) return (ulong)Math.Round(v);
                if (t == typeof(ushort)) return (ushort)Math.Round(v);
                if (t == typeof(sbyte)) return (sbyte)Math.Round(v);
                if (t == typeof(double)) return (double)v;
                if (t == typeof(decimal)) return (decimal)v;
                return v; // float
            }
            catch { return null; }
        }

        private static object TryParseNumeric(Type t, string s)
        {
            try
            {
                if (t == typeof(int)) return int.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(double)) return double.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(long)) return long.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(short)) return short.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(byte)) return byte.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(uint)) return uint.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(ulong)) return ulong.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(ushort)) return ushort.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(sbyte)) return sbyte.Parse(s, CultureInfo.InvariantCulture);
                if (t == typeof(decimal)) return decimal.Parse(s, CultureInfo.InvariantCulture);
            }
            catch { }
            return null;
        }
    }
}
