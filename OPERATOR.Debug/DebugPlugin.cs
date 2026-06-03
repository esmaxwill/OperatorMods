using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace OPERATOR.Debug
{
    // Permanent runtime-introspection plugin. Standalone hotkeys (no menu integration):
    //   F3 — attachment catalog dump (write a C# Mod-table skeleton for the whole LoadoutManager.WeaponModInfo catalog → BepInEx/AttachmentModifiers.generated.cs)
    //   F4 — rebuild auto-dump      (toggle: dump gunStats after every WeaponMods rebuild — the build-funnel differential)
    //   F5 — weapon stat dump       (equipped weapon's gunStats + each CurrentMods entry's Weight/Ergonomics/Recoil)
    //   F6 — sentinel toggle        (inject absurd Weight/Ergonomics/Recoil into the equipped mods, then fire to test for a consumer; press again to restore)
    //   F7 — network snapshot      (NetworkServer/Client/Manager/aoi/local PlayerMaster)
    //   F8 — Il2Cpp self-test      (can a mod-defined type be an Il2Cpp generic arg? + raw-pack primitives)
    //   F9 — reflection dumper     (dump the interop-proxy fields/props of the local player + manager)
    //   F10 — raw-message receive   (can a mod receive a custom raw message with no mod-defined type?)
    //   F11 — NetMessenger broadcast (MessagePack round-trip: Broadcast a payload, log the receipt)
    //   F12 — NetMessenger direct    (SendTo self: host routes it back, logs the receipt)
    // All output goes to the BepInEx log (BepInEx/LogOutput.log).
    [BepInPlugin("com.operator.debug", "OPERATOR Debug", "0.1.0")]
    public class DebugPlugin : BasePlugin
    {
        internal static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            Logger.LogInfo("OPERATOR Debug loaded. Hotkeys: F3 attachment catalog dump, F4 rebuild auto-dump, F5 weapon stat dump, F6 sentinel toggle, F7 net snapshot, F8 Il2Cpp self-test, F9 reflection dump, F10 raw receive, F11 NetMessenger broadcast, F12 NetMessenger direct-to-self.");
            ClassInjector.RegisterTypeInIl2Cpp<DebugBehaviour>();
            AddComponent<DebugBehaviour>();
            new Harmony("com.operator.debug").PatchAll();
        }
    }
}
