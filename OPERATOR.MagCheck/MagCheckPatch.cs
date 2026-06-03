using HarmonyLib;

// The game drives its on-screen mag-check readout through OnScreenNotification.SetMagCheckString,
// so patching it gives us the "player checked their mag" event. We ignore the game's string and
// show our own coarse estimate read from the local equipped weapon's current magazine.
[HarmonyPatch(typeof(OnScreenNotification), nameof(OnScreenNotification.SetMagCheckString))]
public static class MagCheckPatch
{
    private static void Postfix(string str)
    {
        // SetMagCheckString("") is the game clearing the readout — not an actual check.
        if (string.IsNullOrEmpty(str)) return;

        try
        {
            var pn = GameManager.myPlayerNetworking;
            if (pn == null) return;

            var weapon = pn.c_activeWeapon;                 // local equipped WeaponV3
            var gf = weapon != null ? weapon.gunFunction : null;
            var mag = gf != null ? gf.currentMagazine : null;
            if (mag == null) { MagCheckBehaviour.Show("Mag: Empty"); return; }

            int ammo = mag.ammoAmount;
            int capacity = mag.ammoAmountMax + mag.ammoAmountMagExtension;
            MagCheckBehaviour.Show("Mag: " + Bucket(ammo, capacity));
        }
        catch (System.Exception e)
        {
            MagCheckPlugin.Logger.LogWarning("[MagCheck] " + e.Message);
        }
    }

    // Coarse, deliberately-imprecise readout (the game hides exact counts).
    private static string Bucket(int ammo, int capacity)
    {
        if (ammo <= 0) return "Empty";
        if (capacity <= 0) return ammo.ToString();   // unknown capacity — show the raw count
        float ratio = (float)ammo / capacity;
        if (ratio >= 0.95f) return "Full";
        if (ratio >= 0.55f) return "High";
        if (ratio >= 0.25f) return "~Half";
        return "Low";
    }
}
