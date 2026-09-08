using System.Text.Json;

namespace Dsn.Contracts;

public sealed record Envelope(int Version, string SourceId, JsonElement? SourceDescription,
    string Time, string EventType, IReadOnlyList<string> Workspace);

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
    ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken);
}
public interface IWorkspacePlugin
{
    int ApiVersion { get; }
    IWorkspace Create(WorkspaceServices services);
}
public sealed record WorkspaceServices(IRecordStore Records, IErrorSink Errors);
public sealed record RecordInput(string Workspace, IReadOnlyDictionary<string, JsonElement> Fields);
public sealed record StoredRecord(long Id, string Workspace, IReadOnlyDictionary<string, JsonElement> Fields);
public sealed record RecordQuery(string[]? Workspaces = null, long AfterId = 0, int Limit = 100);
public interface IRecordStore { ValueTask AppendAsync(RecordInput record, CancellationToken cancellationToken = default); }
public interface IRecordQuery
{
    IReadOnlyList<StoredRecord> Query(RecordQuery query);
    IReadOnlyDictionary<string, string[]> Fields();
}
public interface IRecordExporter { string Export(RecordQuery query); }
public sealed record ErrorBody(string Code, string Component, string? SourceId = null, string? Workspace = null, string? Detail = null);
public sealed record ErrorAggregate(ErrorBody Body, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, long Count);
public interface IErrorSink { void Report(ErrorBody body); }
public static class Fields
{
    public static IReadOnlyDictionary<string, JsonElement> From(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value));
}
