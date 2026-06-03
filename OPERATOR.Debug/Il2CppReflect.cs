using System;
using System.Reflection;
using BepInEx.Logging;

namespace OPERATOR.Debug
{
    // General-purpose dumper for Il2CppInterop proxy objects. The interop wrapper is a normal
    // managed object whose public properties/fields mirror the Il2Cpp members, so plain
    // System.Reflection over the proxy type surfaces the live values (each getter thunks to
    // native). Reads are best-effort and individually guarded.
    internal static class Il2CppReflect
    {
        private const int MaxValueLen = 200;

        public static void Dump(object obj, ManualLogSource log)
        {
            if (obj == null) { log.LogInfo("  (null)"); return; }

            var t = obj.GetType();
            log.LogInfo($"-- dump {t.FullName} --");

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                // Skip the interop plumbing pointer if any slips through.
                if (p.Name.StartsWith("NativeFieldInfoPtr") || p.Name.StartsWith("NativeMethodInfoPtr")) continue;
                log.LogInfo($"  .{p.Name} = {Read(() => p.GetValue(obj))}");
            }

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                log.LogInfo($"  #{f.Name} = {Read(() => f.GetValue(obj))}");
            }
        }

        private static string Read(Func<object> getter)
        {
            try
            {
                var v = getter();
                if (v == null) return "null";
                string s = v.ToString();
                return s.Length > MaxValueLen ? s.Substring(0, MaxValueLen) + "…" : s;
            }
            catch (Exception e) { return "<err " + e.GetType().Name + ">"; }
        }
    }
}
