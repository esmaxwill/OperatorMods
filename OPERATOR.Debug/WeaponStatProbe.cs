using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;

namespace OPERATOR.Debug
{
    // Answers the prove-a-negative the static dumps can't: does anything actually CONSUME
    // WeaponMod.Recoil/Weight/Ergonomics? Dumping values only shows state — so this probe
    // intervenes. Two techniques:
    //
    //   (A) Differential dump around the build funnel. WeaponMods.RebuildWeaponWithNetworkPreset
    //       is the single loadout-build path; every attach/detach flows through it. The Postfix
    //       below dumps gunStats + each mod's stat fields after every rebuild (when AutoDump is on).
    //       Attach a vertical foregrip at the modding table and diff the snapshots: if no gunStats
    //       field moves, nothing folds mod stats into the weapon.
    //
    //   (B) Sentinel injection (the actual proof). Slam absurd values into every CurrentMods entry's
    //       Recoil/Weight/Ergonomics, then fire/aim. If felt recoil, handling, or any gunStats field
    //       is unchanged, the fields provably have no gameplay consumer on the equip/rebuild path.
    //       Writes target per-instance WeaponMod objects, NOT the shared gunStats — fully reversible.
    [HarmonyPatch(typeof(WeaponMods), nameof(WeaponMods.RebuildWeaponWithNetworkPreset))]
    public static class RebuildWeaponDumpPatch
    {
        // Off by default so normal play doesn't spam the log; toggle with F4.
        public static bool AutoDump;

        private static void Postfix(WeaponMods __instance)
        {
            if (!AutoDump) return;
            try
            {
                DebugPlugin.Logger.LogInfo("==== [rebuild] RebuildWeaponWithNetworkPreset ran — post-build snapshot ====");
                WeaponStatProbe.DumpWeapon(__instance != null ? __instance.Weapon : null, __instance, DebugPlugin.Logger);
            }
            catch (Exception e) { DebugPlugin.Logger.LogWarning("[rebuild] dump failed: " + e.GetType().Name + ": " + e.Message); }
        }
    }

    internal static class WeaponStatProbe
    {
        private static ManualLogSource Log => DebugPlugin.Logger;

        // Sentinel values chosen to be unmistakable if they ever surface in handling/recoil/UI.
        private const float SentinelWeight = 9999f;
        private const float SentinelErgo = -9999f;
        private const float SentinelRecoil = 9999f;

        // Saved originals so the F6 sentinel is a reversible toggle. Holds the mod refs we touched
        // plus their pre-injection (Weight, Ergonomics, Recoil).
        private static readonly List<(WeaponMod mod, float w, float e, float r)> _saved = new();
        private static bool _injected;

        // ---- F5: manual snapshot of the local equipped weapon ----
        public static void DumpEquipped()
        {
            Log.LogInfo("==== [F5] WEAPON STAT DUMP ====");
            var w = GetEquippedWeapon();
            if (w == null) { Log.LogInfo("  no equipped weapon (GameManager.myPlayerNetworking.c_activeWeapon is null)."); return; }
            DumpWeapon(w, w.mods, Log);
        }

        // ---- F6: inject/restore the sentinel ----
        public static void ToggleSentinel()
        {
            Log.LogInfo("==== [F6] SENTINEL " + (_injected ? "RESTORE" : "INJECT") + " ====");
            if (_injected) { Restore(); return; }

            var w = GetEquippedWeapon();
            var list = w != null && w.mods != null ? w.mods.CurrentMods : null;
            if (list == null) { Log.LogInfo("  no equipped weapon / CurrentMods; nothing to inject."); return; }

            _saved.Clear();
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                var m = list[i];
                if (m == null) continue;
                _saved.Add((m, m.Weight, m.Ergonomics, m.Recoil));
                m.Weight = SentinelWeight;
                m.Ergonomics = SentinelErgo;
                m.Recoil = SentinelRecoil;
            }
            _injected = true;
            Log.LogInfo($"  injected sentinels into {_saved.Count} mod(s): Weight={SentinelWeight}, Ergonomics={SentinelErgo}, Recoil={SentinelRecoil}.");
            Log.LogInfo("  NOW: fire and aim the weapon. If recoil/handling/UI is unchanged, these fields have no consumer on this path.");
            Log.LogInfo("  (Press F6 again to restore. A rebuild at the modding table re-instantiates mods and discards the sentinels.)");
        }

        private static void Restore()
        {
            int ok = 0;
            foreach (var s in _saved)
            {
                try { if (s.mod != null) { s.mod.Weight = s.w; s.mod.Ergonomics = s.e; s.mod.Recoil = s.r; ok++; } }
                catch (Exception e) { Log.LogWarning("  restore failed for one mod: " + e.Message); }
            }
            _saved.Clear();
            _injected = false;
            Log.LogInfo($"  restored original stat values on {ok} mod(s).");
        }

        // ---- shared dump ----
        public static void DumpWeapon(WeaponV3 w, WeaponMods mods, ManualLogSource log)
        {
            if (w == null) { log.LogInfo("  weapon is null."); return; }

            log.LogInfo("-- gunStats (the actual weapon stat block; shared asset) --");
            if (w.gunStats != null) Il2CppReflect.Dump(w.gunStats, log);
            else log.LogInfo("  gunStats is null.");

            var list = mods != null ? mods.CurrentMods : null;
            if (list == null) { log.LogInfo("-- CurrentMods: null --"); return; }

            int n = list.Count;
            log.LogInfo($"-- CurrentMods: {n} attachment(s) --");
            for (int i = 0; i < n; i++)
            {
                var m = list[i];
                if (m == null) { log.LogInfo($"  [{i}] null"); continue; }
                string name = SafeName(m);
                // The three fields under investigation, side by side with the slot type.
                log.LogInfo($"  [{i}] {name}  type={m.attachmentType}  Weight={m.Weight}  Ergonomics={m.Ergonomics}  Recoil={m.Recoil}");
            }
        }

        private static string SafeName(WeaponMod m)
        {
            try { var s = m.WeaponModName(); if (!string.IsNullOrEmpty(s)) return s; } catch { }
            try { if (!string.IsNullOrEmpty(m.DisplayNameOverride)) return m.DisplayNameOverride; } catch { }
            return "(unnamed)";
        }

        private static WeaponV3 GetEquippedWeapon()
        {
            try
            {
                var pn = GameManager.myPlayerNetworking;   // same accessor MagCheck uses
                return pn != null ? pn.c_activeWeapon : null;
            }
            catch (Exception e) { Log.LogWarning("  GetEquippedWeapon failed: " + e.Message); return null; }
        }
    }
}
