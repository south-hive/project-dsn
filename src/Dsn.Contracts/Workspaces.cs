using System.Text.Json;

namespace Dsn.Contracts;

// The façade never exposes Memory/Span over the owned buffer. Every access checks the lease.
public interface IReadOnlyPayload
{
    int Length { get; }
    byte At(int index);
    byte[] Copy();
    string ToUtf8();
    string ToHex();
}
public interface IPayloadLease
{
    string MessageId { get; }
    Envelope Envelope { get; }
    IReadOnlyPayload Payload { get; }
}
public interface IMessageContext
{
    IPayloadLease Checkout();
    bool Checkin(IPayloadLease lease);
}
public interface IWorkspace
{
    string Name { get; }
    /// <summary>
    /// Completion includes all work using message/context/leases. Detached access after completion is forbidden.
    /// The supported Runtime calls Workspaces sequentially; concurrent message calls are not required to be safe.
    /// </summary>
    ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken);
}
public interface IWorkspacePlugin
{
    int ApiVersion { get; }
    IWorkspace Create(WorkspaceServices services);
}
public sealed record WorkspaceServices(IRecordStore Records, IErrorSink Errors);
public interface IWorkspaceRegistration
{
    bool Register(IWorkspace workspace);
}
