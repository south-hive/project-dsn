namespace Dsn.Runtime;

// All operations run under the Runtime admission/lifecycle lock. Owns accepted roots until Take.
internal sealed class FifoMessageQueue(int capacity, long maxBytes)
{
    private readonly Queue<OwnedMessage> items = new();
    private long bytes;
    internal bool Empty => items.Count == 0;
    internal bool CanAccept(int size) => items.Count < capacity && bytes + size <= maxBytes;
    internal void Add(OwnedMessage message) { items.Enqueue(message); bytes += message.Read(b => b.Length); }
    internal OwnedMessage Take() { var message = items.Dequeue(); bytes -= message.Read(b => b.Length); return message; }
    internal void Discard() { while (!Empty) Take().Release(); }
}
