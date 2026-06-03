using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using MessagePack;
using Mirror;
using OPERATOR.Common.Networking;
using UnityEngine;

namespace OPERATOR.Debug
{
    // A plain managed struct the game never serializes — the subject of the F8 test:
    // can Il2CppInterop give a mod-defined type an Il2Cpp class (and therefore a usable
    // closed generic Writer<T>/Reader<T>/GetId<T>)? Pure managed structs have no Il2Cpp
    // type, so Il2CppType.From(typeof(DebugNetMsg)) is expected to fail.
    public struct DebugNetMsg
    {
        public ulong Steam64;
        public float R, G, B;
    }

    // Payloads for the F11/F12 NetMessenger round-trip test. Attribute-based MessagePack contracts.
    [MessagePackObject]
    public class DebugNetPing
    {
        [Key(0)] public int N { get; set; }
        [Key(1)] public string Msg { get; set; }
    }

    [MessagePackObject]
    public class DebugNetState
    {
        [Key(0)] public int V { get; set; }
    }

    public class DebugBehaviour : MonoBehaviour
    {
        public DebugBehaviour(IntPtr ptr) : base(ptr) { }

        private static ManualLogSource Log => DebugPlugin.Logger;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) Safe("F3 attachment catalog dump", () => AttachmentDump.DumpCatalog(Log));
            if (Input.GetKeyDown(KeyCode.F4)) Safe("F4 toggle rebuild auto-dump", ToggleRebuildAutoDump);
            if (Input.GetKeyDown(KeyCode.F5)) Safe("F5 weapon stat dump", WeaponStatProbe.DumpEquipped);
            if (Input.GetKeyDown(KeyCode.F6)) Safe("F6 sentinel toggle", WeaponStatProbe.ToggleSentinel);
            if (Input.GetKeyDown(KeyCode.F7)) Safe("F7 network snapshot", NetworkSnapshot);
            if (Input.GetKeyDown(KeyCode.F8)) Safe("F8 Il2Cpp self-test", Il2CppSelfTest);
            if (Input.GetKeyDown(KeyCode.F9)) Safe("F9 reflection dump", ReflectionDump);
            if (Input.GetKeyDown(KeyCode.F10)) Safe("F10 raw-message receive test", RawMessageReceiveTest);
            if (Input.GetKeyDown(KeyCode.F11)) Safe("F11 NetMessenger broadcast", NetRoundTripTest);
            if (Input.GetKeyDown(KeyCode.F12)) Safe("F12 NetMessenger direct-to-self", SendToSelfTest);
        }

        // ---- F11/F12: NetMessenger round-trip (MessagePack over the raw channel) ----
        private static bool _netRegistered;
        private static int _pingCounter;

        private void EnsureNetRegistered()
        {
            if (_netRegistered) return;
            _netRegistered = true;
            NetMessenger.Register<DebugNetPing>(OnPing);
            NetMessenger.Register<DebugNetState>(OnState, stateful: true);
            Log.LogInfo("[NetMsg] registered DebugNetPing + DebugNetState(stateful)");
        }

        private void NetRoundTripTest()
        {
            Log.LogInfo("==== [F11] NETMESSENGER BROADCAST ROUND-TRIP ====");
            EnsureNetRegistered();
            int n = ++_pingCounter;
            NetMessenger.Broadcast(new DebugNetPing { N = n, Msg = "hello-" + n });
            NetMessenger.Broadcast(new DebugNetState { V = n * 10 });
            Log.LogInfo($"[F11] broadcast DebugNetPing(N={n}) + DebugNetState(V={n * 10}); expect OnPing/OnState below with fromSelf=true now (local echo), and again via relay once other clients connect.");
        }

        private void SendToSelfTest()
        {
            Log.LogInfo("==== [F12] NETMESSENGER DIRECT-TO-SELF ====");
            EnsureNetRegistered();
            ulong me = 0UL;
            try { var pm = PlayerMaster.MyPlayerMaster; if (pm != null) me = pm.NetworkthisPlayerSteam64; } catch { }
            if (me == 0UL) { Log.LogWarning("[F12] local Steam64 is 0 (not in a match yet?); aborting."); return; }
            int n = ++_pingCounter;
            NetMessenger.SendTo(me, new DebugNetPing { N = n, Msg = "direct-" + n });
            Log.LogInfo($"[F12] SendTo(self={me}, DebugNetPing(N={n})); routes host->you, expect OnPing with fromSelf=false (SendTo has no local echo).");
        }

        private static void OnPing(Envelope<DebugNetPing> e)
        {
            Log.LogInfo($"[NetMsg] OnPing: N={e.payload.N} Msg='{e.payload.Msg}' sender={e.senderSteamId} fromSelf={e.fromSelf} broadcast={e.shouldBroadcast}");
        }

        private static void OnState(Envelope<DebugNetState> e)
        {
            Log.LogInfo($"[NetMsg] OnState: V={e.payload.V} sender={e.senderSteamId} fromSelf={e.fromSelf} broadcast={e.shouldBroadcast}");
        }

        // ---- F4: toggle auto-dump of gunStats after every weapon rebuild (the build-funnel differential) ----
        private void ToggleRebuildAutoDump()
        {
            RebuildWeaponDumpPatch.AutoDump = !RebuildWeaponDumpPatch.AutoDump;
            Log.LogInfo($"[F4] rebuild auto-dump = {RebuildWeaponDumpPatch.AutoDump}. " +
                        (RebuildWeaponDumpPatch.AutoDump
                            ? "Now attach/detach a grip at the modding table; gunStats is dumped after each rebuild."
                            : "Disabled."));
        }

        // ---- F7: runtime network snapshot ----
        private void NetworkSnapshot()
        {
            Log.LogInfo("==== [F7] NETWORK SNAPSHOT ====");

            Log.LogInfo($"NetworkServer.active={NetworkServer.active}  activeHost={NetworkServer.activeHost}  exceptionsDisconnect={NetworkServer.exceptionsDisconnect}");
            Log.LogInfo($"NetworkServer.aoi == null ? {NetworkServer.aoi == null}   (null => interest management OFF, everyone observes everything)");

            try
            {
                var conns = NetworkServer.connections;
                Log.LogInfo($"NetworkServer.connections.Count = {(conns != null ? conns.Count : -1)}");
                if (conns != null)
                {
                    foreach (var kv in conns)
                    {
                        var c = kv.Value;
                        Log.LogInfo($"  conn id={c.connectionId} address={c.address} isReady={c.isReady} identityNetId={(c.identity != null ? c.identity.netId : 0u)}");
                    }
                }
            }
            catch (Exception e) { Log.LogWarning("  connections enumerate failed: " + Describe(e)); }

            Log.LogInfo($"NetworkClient.active={NetworkClient.active}  isConnected={NetworkClient.isConnected}");
            try
            {
                var conn = NetworkClient.connection;
                Log.LogInfo($"NetworkClient.connection type = {(conn != null ? conn.GetType().Name : "null")}");
                var lp = NetworkClient.localPlayer;
                Log.LogInfo($"NetworkClient.localPlayer netId = {(lp != null ? lp.netId : 0u)}");
            }
            catch (Exception e) { Log.LogWarning("  client state failed: " + Describe(e)); }

            try
            {
                var nm = NetworkManagerVectorGames.instance;
                Log.LogInfo($"NetworkManagerVectorGames.instance present = {nm != null}");
            }
            catch (Exception e) { Log.LogWarning("  NetworkManagerVectorGames.instance failed: " + Describe(e)); }

            try
            {
                var me = PlayerMaster.MyPlayerMaster;
                if (me == null) Log.LogInfo("PlayerMaster.MyPlayerMaster = null (not spawned yet)");
                else Log.LogInfo($"local PlayerMaster: name='{me.NetworkthisPlayerName}' steam64={me.NetworkthisPlayerSteam64} (0 => SyncVar not resynced yet) uniqueId={me.UniqueID}");
            }
            catch (Exception e) { Log.LogWarning("  PlayerMaster.MyPlayerMaster failed: " + Describe(e)); }
        }

        // ---- F8: can a mod-defined type be an Il2Cpp generic argument? + raw-pack primitives ----
        private void Il2CppSelfTest()
        {
            Log.LogInfo("==== [F8] IL2CPP GENERIC / SERIALIZER SELF-TEST ====");

            // (1) Can the runtime resolve an Il2Cpp class for these types? This is the gate for
            //     forming the closed generics Writer<T>/Reader<T>/GetId<T> under Il2CppInterop.
            TryIl2CppType("mod-defined struct DebugNetMsg", typeof(DebugNetMsg));
            TryIl2CppType("engine struct NetworkPingMessage", typeof(NetworkPingMessage));

            // (2) Control: GetId<T> for an engine message type that the weaver DID serialize.
            try
            {
                ushort id = NetworkMessages.GetId<NetworkPingMessage>();
                Log.LogInfo($"  NetworkMessages.GetId<NetworkPingMessage>() = 0x{id:X4}  (generic path reachable for engine types)");
            }
            catch (Exception e) { Log.LogWarning("  GetId<NetworkPingMessage> THREW: " + Describe(e)); }

            // (3) The recommended-recipe primitive: hand-pack [ushort id][string] into a pooled writer
            //     using only already-instantiated generics (no mod-defined T).
            try
            {
                var w = NetworkWriterPool.Get();
                NetworkWriterExtensions.WriteUShort(w, 0xE001);
                NetworkWriterExtensions.WriteString(w, "operator-debug");
                int pos = w.Position;
                NetworkWriterPool.Return(w);
                Log.LogInfo($"  raw-pack via NetworkWriterPool OK, wrote {pos} bytes (this is the safe send primitive)");
            }
            catch (Exception e) { Log.LogWarning("  raw-pack THREW: " + Describe(e)); }
        }

        private void TryIl2CppType(string label, Type t)
        {
            try
            {
                var c = Il2CppType.From(t, false);
                if (c == null) Log.LogInfo($"  Il2CppType.From({label}) = null  => NO Il2Cpp class; cannot be a Mirror generic arg");
                else Log.LogInfo($"  Il2CppType.From({label}) = {c.FullName}  => usable as a generic arg");
            }
            catch (Exception e) { Log.LogInfo($"  Il2CppType.From({label}) THREW: {Describe(e)}  => cannot be a Mirror generic arg"); }
        }

        // ---- F10 (the "F8b" probe): can a mod RECEIVE a custom raw message with NO mod-defined type? ----
        // The typed Send<T>/RegisterHandler<T> path is dead (F8). This tests the raw alternative:
        //   1. build a NetworkMessageDelegate from a plain mod method via Il2CppInterop delegate marshalling,
        //   2. insert it straight into the static NetworkClient.handlers dict under a mod-owned id,
        //   3. pack [ushort id][string] by hand and run it through Mirror's real dispatch (UnpackAndInvoke),
        //   4. confirm our hand-built handler fires and reads the body — all without any mod-defined Il2Cpp type.
        private const ushort RawMsgId = 0xE001;
        private static bool _rawMsgReceived;

        private void RawMessageReceiveTest()
        {
            Log.LogInfo("==== [F10] RAW CUSTOM-MESSAGE RECEIVE TEST ====");
            Log.LogInfo($"NetworkClient.active={NetworkClient.active} isConnected={NetworkClient.isConnected}");

            // (1) Build a NetworkMessageDelegate from a mod method (no mod *type* involved) and install it.
            try
            {
                var managed = new Action<NetworkConnection, NetworkReader, int>(OnRawMsg);
                var il2cppDel = DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(managed);
                var handlers = NetworkClient.handlers;
                if (handlers == null) { Log.LogWarning("  NetworkClient.handlers is null (connect/host first); aborting."); return; }
                handlers[RawMsgId] = il2cppDel;
                Log.LogInfo($"  installed hand-built NetworkMessageDelegate into NetworkClient.handlers[0x{RawMsgId:X4}] (no mod type, no RegisterHandler<T>)");
            }
            catch (Exception e) { Log.LogWarning("  delegate build/install THREW: " + Describe(e) + "  => raw receive NOT viable this way"); return; }

            // (2) Hand-pack [ushort id][string] and (3) run it through Mirror's real dispatch.
            try
            {
                _rawMsgReceived = false;
                var w = NetworkWriterPool.Get();
                NetworkWriterExtensions.WriteUShort(w, RawMsgId);
                NetworkWriterExtensions.WriteString(w, "hello-raw-from-mod");
                var seg = w.ToArraySegment();
                var reader = NetworkReaderPool.Get(seg);
                bool ok = NetworkClient.UnpackAndInvoke(reader, 0);   // reads id, looks up handlers[id], invokes it
                NetworkReaderPool.Return(reader);
                NetworkWriterPool.Return(w);
                Log.LogInfo($"  UnpackAndInvoke returned {ok}; handler fired = {_rawMsgReceived}");
                if (_rawMsgReceived)
                    Log.LogInfo("  => VERDICT: raw custom messages WORK — a hand-built delegate dispatches through Mirror with no mod-defined type and no weaver.");
                else
                    Log.LogWarning("  => handler did NOT fire (UnpackAndInvoke may early-out when not active, or the id was not matched); retry while hosting/in a match.");
            }
            catch (Exception e) { Log.LogWarning("  pack/dispatch THREW: " + Describe(e)); }
        }

        // The hand-built handler. Signature must match NetworkMessageDelegate.Invoke(NetworkConnection, NetworkReader, int).
        private static void OnRawMsg(NetworkConnection conn, NetworkReader reader, int channelId)
        {
            _rawMsgReceived = true;
            try
            {
                string body = NetworkReaderExtensions.ReadString(reader);
                Log.LogInfo($"  [handler] RAW MSG RECEIVED: \"{body}\" (channel {channelId})");
            }
            catch (Exception e) { Log.LogWarning("  [handler] body read THREW: " + Describe(e)); }
        }

        // ---- F9: dump the interop-proxy surface of the local player + manager ----
        private void ReflectionDump()
        {
            Log.LogInfo("==== [F9] REFLECTION DUMP ====");
            try
            {
                var me = PlayerMaster.MyPlayerMaster;
                if (me != null)
                {
                    Il2CppReflect.Dump(me, Log);
                    if (me.PlayerSpawnedObject != null) Il2CppReflect.Dump(me.PlayerSpawnedObject, Log);
                }
                else Log.LogInfo("  PlayerMaster.MyPlayerMaster is null; nothing to dump.");

                var nm = NetworkManagerVectorGames.instance;
                if (nm != null) Il2CppReflect.Dump(nm, Log);
            }
            catch (Exception e) { Log.LogWarning("  reflection dump failed: " + Describe(e)); }
        }

        private static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Log.LogError($"[{what}] unhandled: {e.GetType().Name}: {e.Message}");
                // Walk the inner-exception chain — the root cause (e.g. a missing/mismatched
                // dependency behind a TypeInitializationException) lives here.
                for (var inner = e.InnerException; inner != null; inner = inner.InnerException)
                    Log.LogError($"  caused by: {inner.GetType().Name}: {inner.Message}");
                Log.LogError(e.StackTrace ?? "(no stack)");
            }
        }

        private static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;
    }
}
