using Dsn.Contracts;

namespace Dsn.Workspaces;

public sealed class EchoPlugin : IWorkspacePlugin
{
    public int ApiVersion => 1;
    public IWorkspace Create(WorkspaceServices services) => new PayloadWorkspace("echo", services.Records, false);
}
public sealed class HexPlugin : IWorkspacePlugin
{
    public int ApiVersion => 1;
    public IWorkspace Create(WorkspaceServices services) => new PayloadWorkspace("hex", services.Records, true);
}
public sealed class PayloadWorkspace(string name, IRecordStore records, bool hex) : IWorkspace
{
    public string Name => name;
    public async ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken)
    {
        var lease = message.Checkout();
        RecordInput record;
        try
        {
            record = new(Name, Fields.From(("message_id", lease.MessageId), ("source_id", lease.Envelope.SourceId),
                ("time", lease.Envelope.Time), ("event_type", lease.Envelope.EventType), ("payload_bytes", lease.Payload.Length),
                (hex ? "payload_hex" : "payload_utf8", hex ? lease.Payload.ToHex() : lease.Payload.ToUtf8())));
        }
        finally { message.Checkin(lease); }
        await records.AppendAsync(record, cancellationToken);
    }
}
