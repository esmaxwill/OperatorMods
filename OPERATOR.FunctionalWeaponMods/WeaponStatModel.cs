using System.Collections.Generic;

// Exposed, read-only computed values per weapon (consumed by future recoil/ADS hooks).
public sealed class WeaponStatModel
{
    public float WeightTotal;
    public float ErgonomicsTotal;
    public float RecoilModifier;   // Σ(mod.Recoil * RecoilFactor); 0 = no attachments contributing
}

// Internal per-weapon working record.
internal sealed class WeaponEntry
{
    public float baseWeight;                 // captured once from the shared asset, before any swap
    public float baseErgo;
    public GunStats clone;                   // per-instance clone assigned to weapon.gunStats
    public readonly WeaponStatModel model = new WeaponStatModel();
}

// Static registry + helpers. Holds ONLY the local player's owned weapons.
public static class FunctionalWeaponMods
{
    // Local owned weapons only. Destroyed-weapon keys stay until ClearAll() (wired to
    // PlayerLifecycle.OnPlayerUnloaded in the plugin) — that is the bounded-leak safety valve.
    // Any future iteration of _entries MUST null-guard each key (Unity '==' treats destroyed as null).
    private static readonly Dictionary<WeaponV3, WeaponEntry> _entries = new Dictionary<WeaponV3, WeaponEntry>();

    // Public read API for future consumers (e.g. RecoilHook).
    public static bool TryGetModel(WeaponV3 weapon, out WeaponStatModel model)
    {
        model = null;
        if (weapon == null) return false;
        if (_entries.TryGetValue(weapon, out var e)) { model = e.model; return true; }
        return false;
    }

    // Precondition: weapon is non-null and live — the caller (the Postfix) guards this.
    // First sight: snapshot baseline Weight/Ergonomics from the (still-shared) gunStats, build the clone.
    internal static WeaponEntry GetOrCreate(WeaponV3 weapon)
    {
        if (_entries.TryGetValue(weapon, out var e)) return e;
        var gs = weapon.gunStats;
        e = new WeaponEntry
        {
            baseWeight = gs != null ? gs.Weight : 0f,
            baseErgo   = gs != null ? gs.Ergonomics : 0f,
            clone      = gs != null ? Clone(gs) : new GunStats(),
        };
        _entries[weapon] = e;
        return e;
    }

    // Reset on player unload to avoid cross-session growth (wired in the plugin's Load).
    public static void ClearAll() => _entries.Clear();

    // MAINTENANCE: copies every GunStats field by hand — Il2CppInterop proxy types have no
    // MemberwiseClone/reflection clone available. If a game update adds a GunStats field, add it
    // here too, or clones will silently drop it (the compiler will NOT warn).
    // Full per-instance copy so all weapon ballistics are preserved; patch only edits Weight/Ergonomics.
    public static GunStats Clone(GunStats s)
    {
        var c = new GunStats();
        c.weaponCaliber             = s.weaponCaliber;
        c.isSuppressed              = s.isSuppressed;
        c.lockBoltOnEmpty           = s.lockBoltOnEmpty;
        c.gunshotRange              = s.gunshotRange;
        c.SuppressedAmount          = s.SuppressedAmount;
        c.MOA                       = s.MOA;
        c.changeFireRateSuppressed  = s.changeFireRateSuppressed;
        c.changeFireRateWithBarrels = s.changeFireRateWithBarrels;
        c.FireRate                  = s.FireRate;
        c.FireRateSuppressed        = s.FireRateSuppressed;
        c.TimeBetweenRounds         = s.TimeBetweenRounds;
        c.fireDelay                 = s.fireDelay;
        c.short_FireRate            = s.short_FireRate;
        c.short_FireRateSuppressed  = s.short_FireRateSuppressed;
        c.medium_FireRate           = s.medium_FireRate;
        c.medium_FireRateSuppressed = s.medium_FireRateSuppressed;
        c.long_FireRate             = s.long_FireRate;
        c.long_FireRateSuppressed   = s.long_FireRateSuppressed;
        c.Weight                    = s.Weight;
        c.Ergonomics                = s.Ergonomics;
        c.fireMode                  = s.fireMode;
        c.fullAuto                  = s.fullAuto;
        c.semiAutomatic             = s.semiAutomatic;
        c.burst                     = s.burst;
        c.burstAmount               = s.burstAmount;
        c.burstFired                = s.burstFired;
        return c;
    }
}
