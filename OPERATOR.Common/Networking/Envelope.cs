namespace OPERATOR.Common.Networking
{
  public struct Envelope<T>
  {
    public ulong senderSteamId;
    public bool shouldBroadcast;
    public T payload;
    public bool fromSelf;
  }
}