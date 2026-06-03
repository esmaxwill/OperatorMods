using System;
using Il2CppInterop.Runtime;            // DelegateSupport
using Il2CppInterop.Runtime.Injection;  // ClassInjector
using Mirror;                           // NetworkConnection, NetworkReader, NetworkMessageDelegate
using UnityEngine;

namespace OPERATOR.Common.Networking
{
  public static partial class NetMessenger
  {
    // The single Mirror message id this framework owns. Every NetMessenger payload rides this
    // one dispatch slot; the inner [ushort typeKey] then selects the registered type.
    private const ushort FrameworkMsgId = 0xE0DB;

    private static bool _typeInjected;
    private static GameObject _host;

    // Built once; (re)installed into the handler dicts by the host's poll loop.
    private static NetworkMessageDelegate _clientReceive;
    private static NetworkMessageDelegate _serverReceive;

    /// <summary>
    /// Idempotently stand up the framework: register the host type with il2cpp, build the two
    /// receive delegates, and spawn the persistent host GameObject. Cheap to call repeatedly —
    /// every public entry point (<see cref="Register{T}"/>, <see cref="Broadcast{T}"/>) calls it
    /// first. Must run on the Unity main thread (ClassInjector + GameObject creation).
    /// </summary>
    private static void EnsureBootstrapped()
    {
      if (_host != null) return;   // already running; NetMessengerHost.Update keeps handlers live

      // 1. Make the host MonoBehaviour known to il2cpp so AddComponent can create it (once per process).
      if (!_typeInjected)
      {
        ClassInjector.RegisterTypeInIl2Cpp<NetMessengerHost>();
        _typeInjected = true;
      }

      // 2. Build the two receive delegates once. They wrap plain static methods and depend on no
      //    game/connection state, so they can exist before we're ever connected. (F10 proved
      //    ConvertDelegate works for a hand-built NetworkMessageDelegate.)
      //    Both share the (NetworkConnection, NetworkReader, int) signature — two *separate*
      //    instances so role is known by *which dict* dispatched: client vs server receive.
      _clientReceive ??= DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(
          new Action<NetworkConnection, NetworkReader, int>(ClientReceive));
      _serverReceive ??= DelegateSupport.ConvertDelegate<NetworkMessageDelegate>(
          new Action<NetworkConnection, NetworkReader, int>(ServerReceive));

      // 3. Spawn a persistent host. Its Update() does the work that can't be one-shot:
      //    (re)install _clientReceive / _serverReceive into NetworkClient.handlers /
      //    NetworkServer.handlers whenever a session is active (those dicts are CLEARED on
      //    shutdown, so install must repeat per connect), plus drive late-joiner replay.
      _host = new GameObject("OPERATOR_NetMessenger");
      UnityEngine.Object.DontDestroyOnLoad(_host);
      _host.AddComponent<NetMessengerHost>();
    }
  }
}