using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Persistence;

// Non-durable adapter for Workspace/View tests. Same scalar and query contract as the journal.
public sealed class MemoryRecordStore(int capacity = 1000, long maxBytes = 4 * 1024 * 1024) : IRecordStore, IRecordReader
{
    private readonly int recordLimit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly long byteLimit = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    private readonly object gate = new();
    private readonly List<StoredRecord> rows = [];
    private long bytes;
    public ValueTask AppendAsync(RecordInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var row = RecordValidation.Freeze(new(rows.Count + 1L, input.Workspace, input.Fields));
            var length = JsonSerializer.SerializeToUtf8Bytes(row, JsonFormat.Options).Length + 1;
            if (length > 1024 * 1024 || rows.Count >= recordLimit || bytes + length > byteLimit) throw new IOException("Record quota exceeded");
            rows.Add(row); bytes += length;
        }
        return ValueTask.CompletedTask;
    }
    public IReadOnlyList<StoredRecord> Query(RecordQuery query)
    {
        RecordValidation.Validate(query);
        lock (gate) return rows.Where(r => r.Id > query.AfterId && (query.Workspaces is null || query.Workspaces.Contains(r.Workspace))).Take(query.Limit).ToArray();
    }
    public IReadOnlyDictionary<string, string[]> Fields()
    {
        lock (gate) return rows.GroupBy(r => r.Workspace).ToDictionary(g => g.Key, g => g.SelectMany(r => r.Fields.Keys).Concat(["id", "workspace"]).Distinct().Order().ToArray());
    }
    public string Export(RecordQuery query) => string.Join('\n', Query(query).Select(r => JsonSerializer.Serialize(r, JsonFormat.Options)));
}
