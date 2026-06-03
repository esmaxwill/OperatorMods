using System;
using System.Collections.Generic;
using BepInEx.Logging;
using MessagePack;
using Mirror;

namespace OPERATOR.Common.Networking
{
  // The registry + public registration/send API. Bootstrap lives in Bootstrapper.cs and the
  // receive/maintenance logic in NetMessenger.Receive.cs — all the same partial class.
  public static partial class NetMessenger
  {
    // Visible, single-channel log ([Info:NetMessenger]) — not UnityEngine.Debug, which lands in a
    // different console source and is easy to miss.
    internal static readonly ManualLogSource Log = Logger.CreateLogSource("NetMessenger");

    private static readonly Dictionary<Type, Registration> _byType = new();
    private static readonly Dictionary<ushort, Registration> _byKey = new();

    private static ushort StableHash16(string s)
    {
      uint hash = 0x811C9DC5;
      for (int i = 0; i < s.Length; i++)
      {
        hash ^= s[i];
        hash *= 0x01000193;
      }
      return (ushort)((hash >> 16) ^ hash);
    }

    private sealed class Registration
    {
      public ushort Key;                 // 16-bit stable hash of the type's full name
      public Type PayloadType;
      public bool Stateful;              // server caches latest-per-sender + replays to late joiners
      public Delegate Handlers;          // a multicast Action<Envelope<T>>

      // Type-erased receive path, built in Register<T> where T is known.
      // (rawBody, senderSteamId, fromSelf, shouldBroadcast) -> deserialize to T, build envelope, invoke handlers.
      public Action<byte[], ulong, bool, bool> Deliver;
    }

    public static void Register<T>(Action<Envelope<T>> handler, bool stateful = false)
    {
      if (handler == null) throw new ArgumentNullException(nameof(handler));
      EnsureBootstrapped();

      var type = typeof(T);

      // Already registered? Just append this handler and upgrade the stateful flag.
      if (_byType.TryGetValue(type, out var reg))
      {
        reg.Handlers = Delegate.Combine(reg.Handlers, handler);
        reg.Stateful |= stateful;
        return;
      }

      // New type. Compute key and check for collision.
      ushort key = StableHash16(type.FullName);
      if (_byKey.TryGetValue(key, out var clash))
        throw new InvalidOperationException(
            $"NetMessenger key collision: '{type.FullName}' and '{clash.PayloadType.FullName}' " +
            $"both hash to 0x{key:X4}. Rename one type.");

      reg = new Registration
      {
        Key = key,
        PayloadType = type,
        Stateful = stateful,
        Handlers = handler,
      };

      reg.Deliver = (body, sender, fromSelf, shouldBroadcast) =>
      {
        T payload = MessagePackSerializer.Deserialize<T>(body);
        var env = new Envelope<T>
        {
          senderSteamId = sender,
          payload = payload,
          fromSelf = fromSelf,
          shouldBroadcast = shouldBroadcast,
        };

        // reg.Handlers is read live, so handlers added later are included.
        (reg.Handlers as Action<Envelope<T>>)?.Invoke(env);
      };

      _byType[type] = reg;
      _byKey[key] = reg;
    }

    /// <summary>Remove a handler previously added with <see cref="Register{T}"/>.</summary>
    public static void Unregister<T>(Action<Envelope<T>> handler)
    {
      if (handler == null) return;
      if (!_byType.TryGetValue(typeof(T), out var reg)) return;
      reg.Handlers = Delegate.Remove(reg.Handlers, handler);
    }

    // How the server should route a message it receives from a client.
    internal enum Route : byte
    {
      Server = 0,     // for the server/host only; not relayed
      Broadcast = 1,  // relay to every other client
      Direct = 2,     // relay to the single client whose player Steam64 == target
    }

    // ---- send API ----

    /// <summary>
    /// Owner-originated: dispatch locally (fromSelf=true) + send to the server, which relays to all
    /// other clients. The common "everyone should know" path.
    /// </summary>
    public static void Broadcast<T>(T payload) => Send(payload, Route.Broadcast, 0UL, echoLocal: true);

    /// <summary>Send only to the server/host; its handler decides what to do. No relay, no local echo.</summary>
    public static void SendToServer<T>(T payload) => Send(payload, Route.Server, 0UL, echoLocal: false);

    /// <summary>
    /// Send to a single player identified by Steam64, routed through the host. Neither sender nor
    /// target need be the host. No local echo (you are not the recipient). If the target isn't
    /// connected, the host drops it.
    /// </summary>
    public static void SendTo<T>(ulong targetSteamId, T payload) => Send(payload, Route.Direct, targetSteamId, echoLocal: false);

    private static void Send<T>(T payload, Route route, ulong target, bool echoLocal)
    {
      EnsureBootstrapped();

      byte[] body = MessagePackSerializer.Serialize(payload);
      _byType.TryGetValue(typeof(T), out var reg);
      ushort key = reg?.Key ?? StableHash16(typeof(T).FullName);
      ulong me = LocalSteamId();

      // Optimistic local echo so the sender reacts to its own message without a round-trip.
      if (echoLocal) reg?.Deliver(body, me, /*fromSelf*/ true, /*shouldBroadcast*/ route == Route.Broadcast);

      if (NetworkClient.active && NetworkClient.connection != null)
        SendFramed(NetworkClient.connection, key, route, me, target, body);
    }

    // Wire frame: [ushort FrameworkMsgId][ushort key][byte route][ulong sender][ulong target][bytes mpBody].
    // The leading FrameworkMsgId is what Mirror dispatches on (→ our installed handler); Mirror consumes
    // it before invoking the handler, so ParseFrame starts reading at `key`.
    private static void SendFramed(NetworkConnection conn, ushort key, Route route, ulong sender, ulong target, byte[] body)
    {
      var w = NetworkWriterPool.Get();
      // try/finally so a throw in any write / marshal / Send still returns the writer to the pool —
      // a leaked pooled writer permanently shrinks Mirror's fixed reuse pool.
      try
      {
        NetworkWriterExtensions.WriteUShort(w, FrameworkMsgId);   // Mirror message id → routes to our handler
        NetworkWriterExtensions.WriteUShort(w, key);              // our per-type message key
        NetworkWriterExtensions.WriteByte(w, (byte)route);
        NetworkWriterExtensions.WriteULong(w, sender);
        NetworkWriterExtensions.WriteULong(w, target);
        NetworkWriterExtensions.WriteBytesAndSize(w, Il2CppBytes.ToIl2Cpp(body));
        conn.Send(w.ToArraySegment(), 0);
      }
      finally
      {
        NetworkWriterPool.Return(w);
      }
    }

    private static ulong LocalSteamId()
    {
      try
      {
        var me = PlayerMaster.MyPlayerMaster;
        return me != null ? me.NetworkthisPlayerSteam64 : 0UL;
      }
      catch { return 0UL; }
    }
  }
}
