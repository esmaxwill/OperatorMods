using System;
using Il2CppInterop.Runtime.Injection;
using OPERATOR.Common.Messages;
using UnityEngine;

namespace OPERATOR.Common
{
    public static class PlayerLifecycle
    {
        public static event Action<PlayerNetworking> OnPlayerSpawned;
        public static event Action OnPlayerUnloaded;

        internal static void NotifySpawned(PlayerNetworking pn)
        {
            // Isolate each subscriber: a throwing one must not stop later subscribers or the
            // ModBus emit below. This is driven from an Update, so an escaping exception would
            // also log every frame until the player state changes.
            Invoke(OnPlayerSpawned, pn);
            try { ModBus.Emit(new PlayerSpawned { Player = pn }); }
            catch (Exception e) { Debug.LogWarning("[PlayerLifecycle] ModBus.Emit(PlayerSpawned) threw: " + e.Message); }
        }

        internal static void NotifyUnloaded()
        {
            Invoke(OnPlayerUnloaded);
            try { ModBus.Emit(new PlayerUnloaded()); }
            catch (Exception e) { Debug.LogWarning("[PlayerLifecycle] ModBus.Emit(PlayerUnloaded) threw: " + e.Message); }
        }

        private static void Invoke(Action<PlayerNetworking> evt, PlayerNetworking pn)
        {
            if (evt == null) return;
            var list = evt.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<PlayerNetworking>)list[i])(pn); }
                catch (Exception e) { Debug.LogWarning("[PlayerLifecycle] OnPlayerSpawned subscriber threw: " + e.Message); }
            }
        }

        private static void Invoke(Action evt)
        {
            if (evt == null) return;
            var list = evt.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action)list[i])(); }
                catch (Exception e) { Debug.LogWarning("[PlayerLifecycle] OnPlayerUnloaded subscriber threw: " + e.Message); }
            }
        }
    }

    public class PlayerLifecycleWatcher : MonoBehaviour
    {
        public PlayerLifecycleWatcher(IntPtr ptr) : base(ptr) { }

        private static PlayerLifecycleWatcher _instance;
        private static bool _injected;
        private PlayerNetworking _lastPlayer;

        public static void EnsureRunning()
        {
            if (_instance != null) return;
            // IL2CPP can't AddComponent a managed MonoBehaviour until its type is injected.
            if (!_injected)
            {
                ClassInjector.RegisterTypeInIl2Cpp<PlayerLifecycleWatcher>();
                _injected = true;
            }
            var go = new GameObject("OPERATOR_PlayerLifecycle");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlayerLifecycleWatcher>();
        }

        private void Update()
        {
            var current = GameManager.myPlayerNetworking;
            if (current == _lastPlayer) return;

            if (current != null)
                PlayerLifecycle.NotifySpawned(current);
            else
                PlayerLifecycle.NotifyUnloaded();

            _lastPlayer = current;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
