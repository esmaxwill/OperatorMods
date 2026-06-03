using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace OPERATOR.Common.Networking
{
  // Bridges managed byte[] (what MessagePack produces/consumes) and the il2cpp byte[] the Mirror
  // proxy expects at WriteBytesAndSize / ReadBytesAndSize. Element-wise copy — fine for the
  // occasional-settings payload sizes this framework targets.
  internal static class Il2CppBytes
  {
    public static Il2CppStructArray<byte> ToIl2Cpp(byte[] b)
    {
      var arr = new Il2CppStructArray<byte>(b.Length);
      for (int i = 0; i < b.Length; i++) arr[i] = b[i];
      return arr;
    }

    public static byte[] ToManaged(Il2CppStructArray<byte> arr)
    {
      int n = arr.Length;
      var b = new byte[n];
      for (int i = 0; i < n; i++) b[i] = arr[i];
      return b;
    }
  }
}
