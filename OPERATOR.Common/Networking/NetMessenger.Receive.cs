using System;
using System.Collections.Generic;
using Mirror;

namespace OPERATOR.Common.Networking
{
  // Session maintenance + the two receive paths. Same partial class as the registry/bootstrap, so
  // these see _byKey / reg.Deliver / SendFramed / Route / _clientReceive / _serverReceive / FrameworkMsgId.
  //
  // MESSAGE FLOW (OPERATOR is always a listen-host: the server is also a player; NetworkClient.connection
  // on the host is an in-process loopback, so the host is "just connection 0" in NetworkServer.connections).
  // Each frame carries a Route the server acts on:
  //
  //   Broadcast (Broadcast<T>):
  //     sender does a LOCAL ECHO (own handlers, fromSelf=true), then -> server,
  //     which RELAYS to every connection EXCEPT the origin. Others (incl. the host's loopback conn)
  //     receive via ClientReceive. => sender via echo, everyone else via relay; never both.
  //
  //   Direct (SendTo<T>(targetSteamId, ...)):
  //     no echo; -> server, which forwards to ONLY the connection whose player Steam64 == target
  //     (that may be the host's own loopback conn). A->host->B works with neither being the host.
  //
  //   Server (SendToServer<T>):
  //     no echo, no relay; the server/host is the recipient, so ServerReceive dispatches it to the
  //     host's local handlers directly. (This is the one route the server delivers locally.)
  //
  //   ServerReceive never double-dispatches a Broadcast/Direct locally — the host already gets those
  //   via echo or relay-to-loopback.
  public static partial class NetMessenger
  {
    // Latest stateful broadcast bodies: typeKey -> (senderSteamId -> mpBody). Replayed to late joiners.
    private static readonly Dictionary<ushort, Dictionary<ulong, byte[]>> _stateCache = new();

    // Connection ids we've already replayed cached state to (so each late joiner gets it once).
    private static readonly HashSet<int> _replayed = new();

    /// <summary>
    /// (Re)assert the framework's handlers and drive late-joiner replay. Called by
    /// <see cref="NetMessengerHost"/>'s throttled Update — the handler dicts are cleared on
    /// shutdown, so installation must repeat per session. Never throws.
    /// </summary>
    internal static void MaintainHandlers()
    {
      try
      {
        // Do nothing while a scene is loading (e.g. mid StartHost) — touching Mirror's generic
        // handlers/connections dictionaries during the transition can hang the il2cpp domain.
        if (NetworkClient.active && !NetworkClient.isLoadingScene)
        {
          var h = NetworkClient.handlers;
          if (h != null && !h.ContainsKey(FrameworkMsgId)) h[FrameworkMsgId] = _clientReceive;
        }

        if (NetworkServer.active && !NetworkServer.isLoadingScene)
        {
          var h = NetworkServer.handlers;
          if (h != null && !h.ContainsKey(FrameworkMsgId)) h[FrameworkMsgId] = _serverReceive;
          // Only enumerate connections when there's actually cached state to replay.
          if (_stateCache.Count > 0) ReplayToLateJoiners();
        }

        // Reset session state when the server goes away so a fresh host session starts clean.
        // _stateCache must be cleared too — otherwise the previous session's cached bodies (under
        // the previous senders' Steam64 ids) replay to the next session's late joiners.
        if (!NetworkServer.active)
        {
          if (_replayed.Count > 0) _replayed.Clear();
          if (_stateCache.Count > 0) _stateCache.Clear();
        }
      }
      catch (Exception e) { Log.LogWarning("MaintainHandlers: " + e.Message); }
    }

    private static void ReplayToLateJoiners()
    {
      var conns = NetworkServer.connections;
      if (conns == null) return;

      foreach (var kv in conns)
      {
        var conn = kv.Value;
        if (conn == null || !conn.isReady) continue;
        // Skip the host's own loopback connection (id 0). The host populated _stateCache via its
        // own echo/relay; it is never a "late joiner" to itself, and replaying here would
        // re-dispatch the host's own cached state back to it (fromSelf=false → possible double-apply).
        if (kv.Key == 0) continue;
        if (!_replayed.Add(kv.Key)) continue;   // already replayed to this connection id

        foreach (var byType in _stateCache)
          foreach (var bySender in byType.Value)
            SendFramed(conn, byType.Key, Route.Broadcast, bySender.Key, 0UL, bySender.Value);
      }

      // Forget ids that have disconnected so a reused id replays again.
      if (_replayed.Count > 0)
      {
        var stale = new List<int>();
        foreach (var id in _replayed) if (!conns.ContainsKey(id)) stale.Add(id);
        for (int i = 0; i < stale.Count; i++) _replayed.Remove(stale[i]);
      }
    }

    // Installed into NetworkClient.handlers[FrameworkMsgId]. Runs in Mirror's dispatch loop — never throws.
    private static void ClientReceive(NetworkConnection conn, NetworkReader reader, int channelId)
    {
      try
      {
        ParseFrame(reader, out ushort key, out Route route, out ulong sender, out _, out byte[] body);
        if (_byKey.TryGetValue(key, out var reg))
          reg.Deliver(body, sender, /*fromSelf*/ false, /*shouldBroadcast*/ route == Route.Broadcast);
      }
      catch (Exception e) { Log.LogWarning("ClientReceive: " + e.Message); }
    }

    // Installed into NetworkServer.handlers[FrameworkMsgId]. conn is the sending NetworkConnectionToClient.
    private static void ServerReceive(NetworkConnection conn, NetworkReader reader, int channelId)
    {
      try
      {
        ParseFrame(reader, out ushort key, out Route route, out ulong wireSender, out ulong target, out byte[] body);
        ulong sender = ServerResolveSender(conn, wireSender);   // authoritative; fall back to wire value
        _byKey.TryGetValue(key, out var reg);
        switch (route)
        {
          case Route.Broadcast:
            // Cache stateful state so late joiners can be brought up to date, then relay to all-but-origin.
            if (reg != null && reg.Stateful)
            {
              if (!_stateCache.TryGetValue(key, out var bySender))
              {
                bySender = new Dictionary<ulong, byte[]>();
                _stateCache[key] = bySender;
              }
              bySender[sender] = body;
            }

            var conns = NetworkServer.connections;
            if (conns != null)
              foreach (var kv in conns)
                if (kv.Value != null && kv.Value.connectionId != conn.connectionId)
                  SendFramed(kv.Value, key, Route.Broadcast, sender, 0UL, body);
            break;

          case Route.Direct:
            // Forward to only the target player's connection (may be the host's own loopback conn).
            var dest = FindConnectionBySteam64(target);
            if (dest != null) SendFramed(dest, key, Route.Direct, sender, target, body);
            // else: target not connected — drop.
            break;

          case Route.Server:
            // The server/host is the recipient. No relay; dispatch to local handlers. (No double-fire:
            // server routing never echoes and never relays.)
            reg?.Deliver(body, sender, /*fromSelf*/ false, /*shouldBroadcast*/ false);
            break;
        }
      }
      catch (Exception e) { UnityEngine.Debug.LogWarning("[NetMessenger] ServerReceive: " + e.Message); }
    }

    private static void ParseFrame(NetworkReader reader, out ushort key, out Route route, out ulong sender, out ulong target, out byte[] body)
    {
      key = NetworkReaderExtensions.ReadUShort(reader);
      route = (Route)NetworkReaderExtensions.ReadByte(reader);
      sender = NetworkReaderExtensions.ReadULong(reader);
      target = NetworkReaderExtensions.ReadULong(reader);
      body = Il2CppBytes.ToManaged(NetworkReaderExtensions.ReadBytesAndSize(reader));
    }

    private static NetworkConnectionToClient FindConnectionBySteam64(ulong steam64)
    {
      if (steam64 == 0UL) return null;
      var conns = NetworkServer.connections;
      if (conns == null) return null;
      foreach (var kv in conns)
      {
        var c = kv.Value;
        if (c != null && SteamIdOf(c) == steam64) return c;
      }
      return null;
    }

    // Resolve a connection's owned-player Steam64 (server-authoritative). 0 if not resolvable.
    private static ulong SteamIdOf(NetworkConnection conn)
    {
      try
      {
        var ctc = conn.TryCast<NetworkConnectionToClient>();
        var id = ctc != null ? ctc.identity : null;
        if (id != null)
        {
          var pm = id.GetComponent<PlayerMaster>();
          if (pm != null) return pm.NetworkthisPlayerSteam64;
        }
      }
      catch { /* fall through */ }
      return 0UL;
    }

    private static ulong ServerResolveSender(NetworkConnection conn, ulong fallback)
    {
      ulong resolved = SteamIdOf(conn);
      return resolved != 0UL ? resolved : fallback;
    }
  }
}
