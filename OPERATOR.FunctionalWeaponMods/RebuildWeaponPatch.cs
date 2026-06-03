using System;
using HarmonyLib;

// Postfix on the single weapon-build funnel. Fires on every client for ALL players' weapons
// (JSON_MOD_STRING SyncVar hook), so the isOwned guard restricts work to the local player's weapons.
[HarmonyPatch(typeof(WeaponMods), nameof(WeaponMods.RebuildWeaponWithNetworkPreset))]
public static class RebuildWeaponPatch
{
    private static void Postfix(WeaponMods __instance)
    {
        if (FunctionalWeaponModsPlugin.Enabled == null || !FunctionalWeaponModsPlugin.Enabled.Value) return;

        try
        {
            // isOwned / Weapon are IL2CPP property reads that can throw on a destroyed object — keep inside the try.
            if (__instance == null || !__instance.isOwned) return;   // local player's weapons only

            var weapon = __instance.Weapon;
            if (weapon == null) return;

            var entry = FunctionalWeaponMods.GetOrCreate(weapon);

            float w = 0f, e = 0f, r = 0f;
            var mods = __instance.CurrentMods;
            if (mods != null)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (m == null) continue;
                    w += m.Weight;
                    e += m.Ergonomics;
                    r += m.Recoil;
                }
            }

            // Always recompute from the captured baseline so repeated rebuilds never accumulate.
            entry.clone.Weight     = entry.baseWeight + w * FunctionalWeaponModsPlugin.WeightFactor.Value;
            entry.clone.Ergonomics = entry.baseErgo   + e * FunctionalWeaponModsPlugin.ErgoFactor.Value;

            entry.model.WeightTotal     = entry.clone.Weight;
            entry.model.ErgonomicsTotal = entry.clone.Ergonomics;
            entry.model.RecoilModifier  = r * FunctionalWeaponModsPlugin.RecoilFactor.Value;

            // Swap in the per-instance clone (never mutate the shared gunStats asset). Reasserted each
            // rebuild so the per-instance stats survive any code that might reset the reference.
            weapon.gunStats = entry.clone;

            // TODO(hook): scale the real recoil by entry.model.RecoilModifier — see RecoilHook.cs.
        }
        catch (Exception ex)
        {
            FunctionalWeaponModsPlugin.Logger.LogWarning("[FunctionalWeaponMods] " + ex.Message);
        }
    }
}
