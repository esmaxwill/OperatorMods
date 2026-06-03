using System;
using System.Collections.Generic;

namespace OPERATOR.Common
{
    public static class ModBus
    {
        private static readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        public static void On<T>(Action<T> handler)
        {
            var type = typeof(T);
            _handlers[type] = _handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Off<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var existing)) return;
            var updated = Delegate.Remove(existing, handler);
            if (updated == null) _handlers.Remove(type);
            else _handlers[type] = updated;
        }

        public static void Emit<T>(T message)
        {
            if (!_handlers.TryGetValue(typeof(T), out var handler)) return;

            // Invoke each subscriber in isolation so one throwing handler cannot starve the rest
            // of the multicast chain (a single buggy mod must not break the bus for every other mod).
            var list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<T>)list[i])(message); }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[ModBus] {typeof(T).Name} handler threw: {e.Message}");
                }
            }
        }
    }
}
