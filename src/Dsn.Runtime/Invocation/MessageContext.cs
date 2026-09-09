using Dsn.Contracts;

namespace Dsn.Runtime;

internal sealed class MessageContext(OwnedMessage message) : IMessageContext, IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<Lease> leases = [];
    private bool closed;
    public IPayloadLease Checkout() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); var lease = message.Checkout(); leases.Add(lease); return lease; } }
    public bool Checkin(IPayloadLease lease)
    {
        lock (gate) { if (lease is not Lease own || !leases.Contains(own)) throw new ArgumentException("Foreign lease"); return own.Return(); }
    }
    public void Dispose() { lock (gate) { closed = true; foreach (var lease in leases) lease.Return(); } }
}
