// Extension point for making attachment Recoil affect the ACTUAL recoil the player feels.
//
// FunctionalWeaponMods computes a per-weapon RecoilModifier (Σ mod.Recoil * RecoilFactor). The real
// recoil is the camera/animation system (RecoilPattern / RecoilAnimData), NOT gunStats — there is no
// gunStats.Recoil field. To wire it, Harmony-patch the per-shot recoil-application method on the local
// weapon and scale its magnitude by GetRecoilModifier(weapon). Confirm the target method at runtime
// with the OPERATOR.Debug probe before patching.
//
// Template:
//   [HarmonyPatch(typeof(/* recoil applier */), nameof(/* apply method */))]
//   public static class RecoilApplyHook
//   {
//       static void Prefix(/* component */ __instance, ref /* magnitude type */ value)
//       {
//           // resolve the weapon for __instance, then:
//           // value *= 1f + RecoilHook.GetRecoilModifier(weapon);
//       }
//   }
public static class RecoilHook
{
    // Computed recoil modifier for a weapon (0 = no attachment contribution). Returns 0 if unknown.
    public static float GetRecoilModifier(WeaponV3 weapon)
        => FunctionalWeaponMods.TryGetModel(weapon, out var m) ? m.RecoilModifier : 0f;
}
