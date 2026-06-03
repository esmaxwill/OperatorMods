using System;
using UnityEngine;

namespace OPERATOR.Common.Networking
{
  /// <summary>
  /// Persistent host behaviour for <see cref="NetMessenger"/>, spawned once by EnsureBootstrapped.
  /// Its throttled Update calls <see cref="NetMessenger.MaintainHandlers"/> to (re)install the
  /// framework's receive delegates whenever a session is active (the NetworkClient/Server.handlers
  /// dicts are cleared on shutdown) and to drive late-joiner replay.
  /// </summary>
  public class NetMessengerHost : MonoBehaviour
  {
    // Required for il2cpp-injected MonoBehaviours (same pattern as ModSettingsMenu / PartyLight).
    public NetMessengerHost(IntPtr ptr) : base(ptr) { }

    private const float PollInterval = 0.5f;   // ~2 Hz
    private float _timer;

    private void Update()
    {
      _timer += Time.deltaTime;
      if (_timer < PollInterval) return;
      _timer -= PollInterval;

      NetMessenger.MaintainHandlers();
    }
  }
}
