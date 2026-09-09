using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Persistence;

internal static class JsonFormat
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}
// Single-process append journal. Successful Append includes Flush(true), then query visibility.
public sealed class JournalStore : IRecordStore, IRecordReader, IDisposable
{
    private readonly object gate = new();
    private readonly List<StoredRecord> rows = [];
    private readonly FileStream stream;
    private readonly long maxBytes;
    private readonly int maxRecords;
    private bool disposed, faulted;
    private long nextId = 1;
    public JournalStore(string path, long maxBytes = 256 * 1024 * 1024, int maxRecords = 100000)
    {
        if (maxBytes < 1 || maxRecords < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        this.maxBytes = maxBytes; this.maxRecords = maxRecords;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        stream = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            if (stream.Length > maxBytes) throw new InvalidDataException("Journal exceeds configured byte quota");
            // Bound individual records while recovering, and truncate only an incomplete final frame.
            var line = new List<byte>(); long committed = 0;
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b != 10) { if (line.Count >= 1024 * 1024) throw new InvalidDataException("Journal record too large"); line.Add((byte)b); continue; }
                var row = JsonSerializer.Deserialize<StoredRecord>(line.ToArray(), JsonFormat.Options) ?? throw new InvalidDataException("Null record");
                if (row.Id != nextId || rows.Count >= maxRecords) throw new InvalidDataException("Invalid journal sequence/quota");
                rows.Add(RecordValidation.Freeze(row)); nextId++; line.Clear(); committed = stream.Position;
            }
            if (committed != stream.Length) { stream.SetLength(committed); stream.Flush(true); }
            stream.Position = stream.Length;
        }
        catch { stream.Dispose(); throw; }
    }
    public ValueTask AppendAsync(RecordInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) throw new IOException("Journal is faulted; restart required");
            var row = RecordValidation.Freeze(new(nextId, input.Workspace, input.Fields));
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row, JsonFormat.Options) + "\n");
            if (bytes.Length > 1024 * 1024 || stream.Length + bytes.Length > maxBytes || rows.Count >= maxRecords) throw new IOException("Record quota exceeded");
            var start = stream.Position;
            try { stream.Write(bytes); stream.Flush(true); }
            catch { faulted = true; try { stream.SetLength(start); stream.Flush(true); } catch (IOException) { } throw; }
            rows.Add(row); nextId++;
        }
        return ValueTask.CompletedTask;
    }
    public IReadOnlyList<StoredRecord> Query(RecordQuery query)
    {
        RecordValidation.Validate(query);
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return rows.Where(r => r.Id > query.AfterId && (query.Workspaces is null || query.Workspaces.Contains(r.Workspace))).Take(query.Limit).ToArray(); }
    }
    public IReadOnlyDictionary<string, string[]> Fields()
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return rows.GroupBy(r => r.Workspace).ToDictionary(g => g.Key, g => g.SelectMany(r => r.Fields.Keys).Concat(["id", "workspace"]).Distinct().Order().ToArray()); }
    }
    public string Export(RecordQuery query) => string.Join('\n', Query(query).Select(r => JsonSerializer.Serialize(r, JsonFormat.Options)));
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; stream.Dispose(); } }
}
