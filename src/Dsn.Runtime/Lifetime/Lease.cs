using System.Text;
using Dsn.Contracts;

namespace Dsn.Runtime;

internal sealed class Lease(OwnedMessage message) : IPayloadLease, IReadOnlyPayload
{
    private readonly object gate = new();
    private bool returned;
    private T Read<T>(Func<byte[], T> read) { lock (gate) { ObjectDisposedException.ThrowIf(returned, this); return message.Read(read); } }
    public string MessageId => Read(_ => message.Id);
    public Envelope Envelope => Read(_ => message.Envelope);
    public IReadOnlyPayload Payload { get { Read(_ => 0); return this; } }
    public int Length => Read(b => b.Length);
    public byte At(int index) => Read(b => b[index]);
    public byte[] Copy() => Read(b => b.ToArray());
    public string ToUtf8() => Read(Encoding.UTF8.GetString);
    public string ToHex() => Read(b => Convert.ToHexString(b).ToLowerInvariant());
    internal bool Return() { lock (gate) { if (returned) return false; returned = true; message.Release(); return true; } }
}
