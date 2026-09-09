using System.Text.Json;

namespace Dsn.Contracts;

public sealed record RecordInput(string Workspace, IReadOnlyDictionary<string, JsonElement> Fields);
public sealed record StoredRecord(long Id, string Workspace, IReadOnlyDictionary<string, JsonElement> Fields);
public sealed record RecordQuery(string[]? Workspaces = null, long AfterId = 0, int Limit = 100);
public interface IRecordStore { ValueTask AppendAsync(RecordInput record, CancellationToken cancellationToken = default); }
public interface IRecordQuery
{
    IReadOnlyList<StoredRecord> Query(RecordQuery query);
    IReadOnlyDictionary<string, string[]> Fields();
}
public interface IRecordReader : IRecordQuery, IRecordExporter { }
public interface IRecordExporter { string Export(RecordQuery query); }
public static class Fields
{
    public static IReadOnlyDictionary<string, JsonElement> From(params (string Name, object? Value)[] fields) =>
        fields.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value));
}
