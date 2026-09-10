using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Dsn.Contracts;
using Microsoft.Data.Sqlite;

namespace Dsn.Persistence;

// One local DSN writer owns this connection; callbacks never escape the gate.
// Quotas count logical encoded content, not SQLite page/index/WAL overhead.
public sealed partial class SqliteStore : IRecordStore, IRecordReader, IRawArchive, ISavedViewStore, IDisposable
{
    private static readonly object providerGate = new();
    private static bool providerReady;
    private readonly object gate = new();
    private readonly SqliteConnection db;
    private readonly FileStream owner;
    private readonly long recordBytes, rawBytes;
    private readonly int recordCapacity, rawCapacity;
    private bool disposed;
    public string NodeId { get; }
    public string SqliteVersion { get { lock (gate) return db.ServerVersion; } }

    public SqliteStore(string path, long recordBytes = 256 * 1024 * 1024, int recordCapacity = 100000,
        long rawBytes = 256 * 1024 * 1024, int rawCapacity = 100000)
    {
        if (recordBytes < 1 || rawBytes < 1 || recordCapacity < 1 || rawCapacity < 1) throw new ArgumentException("Invalid storage quota");
        this.recordBytes = recordBytes; this.recordCapacity = recordCapacity; this.rawBytes = rawBytes; this.rawCapacity = rawCapacity;
        lock (providerGate)
        {
            if (!providerReady)
            {
                // No bundle initializer: Termux uses its bionic sqlite3, desktop deployments ship e_sqlite3.
                SQLitePCL.raw.SetProvider(RuntimeInformation.RuntimeIdentifier.Contains("bionic", StringComparison.Ordinal) || OperatingSystem.IsAndroid()
                    ? new SQLitePCL.SQLite3Provider_sqlite3() : new SQLitePCL.SQLite3Provider_e_sqlite3());
                providerReady = true;
            }
        }
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        owner = new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        db = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            db.Open();
            Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            var version = Convert.ToInt32(Scalar("PRAGMA user_version"));
            if (version is not (0 or 1)) throw new InvalidDataException("Unsupported DSN SQLite schema");
            Execute("""
                CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS usage(kind TEXT PRIMARY KEY, bytes INTEGER NOT NULL, count INTEGER NOT NULL);
                INSERT OR IGNORE INTO usage VALUES('records',0,0),('raw',0,0);
                CREATE TABLE IF NOT EXISTS records(id INTEGER PRIMARY KEY AUTOINCREMENT, record_id TEXT NOT NULL UNIQUE,
                    workspace TEXT NOT NULL, fields TEXT NOT NULL, source_id TEXT, event_time TEXT, bytes INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS records_workspace_id ON records(workspace,id);
                CREATE INDEX IF NOT EXISTS records_source_time ON records(source_id,event_time);
                CREATE TABLE IF NOT EXISTS raw(id INTEGER PRIMARY KEY AUTOINCREMENT, message_id TEXT NOT NULL UNIQUE,
                    received_at TEXT NOT NULL, envelope TEXT NOT NULL, payload BLOB NOT NULL, bytes INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS record_fields(workspace TEXT NOT NULL, name TEXT NOT NULL, PRIMARY KEY(workspace,name));
                CREATE TABLE IF NOT EXISTS views(user TEXT NOT NULL, name TEXT NOT NULL, definition TEXT NOT NULL, PRIMARY KEY(user,name));
                PRAGMA user_version=1;
                """);
            Execute("INSERT OR IGNORE INTO metadata VALUES('node_id',$node)", ("$node", Guid.NewGuid().ToString("N")));
            NodeId = (string)Scalar("SELECT value FROM metadata WHERE key='node_id'")!;
            ValidateUsage("records", recordBytes, recordCapacity); ValidateUsage("raw", rawBytes, rawCapacity);
        }
        catch { db.Dispose(); owner.Dispose(); throw; }
    }
    private SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private int Execute(string sql, params (string Name, object? Value)[] args)
    { using var c = Command(sql, args); return c.ExecuteNonQuery(); }
    private object? Scalar(string sql, params (string Name, object? Value)[] args)
    { using var c = Command(sql, args); return c.ExecuteScalar(); }
    private void Transaction(Action action, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            Execute("BEGIN IMMEDIATE");
            try { action(); token.ThrowIfCancellationRequested(); Execute("COMMIT"); }
            catch { Execute("ROLLBACK"); throw; }
        }
        catch (SqliteException e) { throw new IOException("SQLite operation failed", e); }
    }
    private void ValidateUsage(string kind, long bytes, int count)
    {
        using var c = Command("SELECT bytes,count FROM usage WHERE kind=$kind", ("$kind", kind)); using var r = c.ExecuteReader();
        if (!r.Read() || r.GetInt64(0) > bytes || r.GetInt64(1) > count) throw new InvalidDataException("Existing data exceeds configured quota");
    }
    private void Charge(string kind, int bytes)
    {
        var isRaw = kind == "raw";
        if (Execute("UPDATE usage SET bytes=bytes+$size,count=count+1 WHERE kind=$kind AND bytes <= $max-$size AND count < $count",
            ("$size", bytes), ("$kind", kind), ("$max", isRaw ? rawBytes : recordBytes), ("$count", isRaw ? rawCapacity : recordCapacity)) != 1)
            throw new IOException("Storage quota exceeded");
    }
    public ValueTask AppendAsync(RecordInput record, CancellationToken cancellationToken = default)
    {
        lock (gate) Transaction(() => InsertRecord(record), cancellationToken);
        return ValueTask.CompletedTask;
    }
    private void InsertRecord(RecordInput input, long? id = null)
    {
        var fields = input.Fields.ToDictionary(p => p.Key, p => p.Value.Clone());
        // Store identity is assigned here and never inherited from an application payload.
        fields["node_id"] = JsonSerializer.SerializeToElement(NodeId);
        var recordId = Guid.NewGuid().ToString("N"); fields["record_id"] = JsonSerializer.SerializeToElement(recordId);
        var frozen = RecordValidation.Freeze(new(id ?? 0, input.Workspace, fields));
        var json = JsonSerializer.Serialize(frozen.Fields, JsonFormat.Options);
        int bytes = Encoding.UTF8.GetByteCount(json) + Encoding.UTF8.GetByteCount(input.Workspace);
        if (bytes > 1024 * 1024) throw new ArgumentException("Record too large");
        Charge("records", bytes);
        string? Text(string key) => fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        Execute("INSERT INTO records(id,record_id,workspace,fields,source_id,event_time,bytes) VALUES($id,$rid,$w,$f,$s,$t,$b)",
            ("$id", id), ("$rid", recordId), ("$w", input.Workspace), ("$f", json), ("$s", Text("source_id")), ("$t", Text("time")), ("$b", bytes));
        foreach (var name in fields.Keys)
            Execute("INSERT OR IGNORE INTO record_fields VALUES($w,$n)", ("$w", input.Workspace), ("$n", name));
    }
    public IReadOnlyList<StoredRecord> Query(RecordQuery query)
    {
        RecordValidation.Validate(query);
        lock (gate)
        {
            var args = new List<(string, object?)> { ("$after", query.AfterId), ("$limit", query.Limit) };
            string scope = "";
            if (query.Workspaces is { } workspaces)
            {
                scope = " AND workspace IN (" + string.Join(',', workspaces.Select((w, i) => "$w" + i)) + ")";
                for (int i = 0; i < workspaces.Length; i++) args.Add(("$w" + i, workspaces[i]));
            }
            using var command = Command("SELECT id,workspace,fields FROM records WHERE id>$after" + scope + " ORDER BY id LIMIT $limit", args.ToArray());
            using var reader = command.ExecuteReader(); var result = new List<StoredRecord>();
            while (reader.Read()) result.Add(RecordValidation.Freeze(new(reader.GetInt64(0), reader.GetString(1),
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(reader.GetString(2), JsonFormat.Options)!)));
            return result;
        }
    }
    public IReadOnlyDictionary<string, string[]> Fields()
    {
        lock (gate)
        {
            using var command = Command("SELECT workspace,name FROM record_fields ORDER BY workspace,name"); using var reader = command.ExecuteReader();
            var fields = new Dictionary<string, List<string>>();
            while (reader.Read()) { var w = reader.GetString(0); if (!fields.TryGetValue(w, out var list)) fields[w] = list = ["id", "workspace"]; list.Add(reader.GetString(1)); }
            return fields.ToDictionary(p => p.Key, p => p.Value.Order().ToArray());
        }
    }
    public string Export(RecordQuery query) => string.Join('\n', Query(query).Select(r => JsonSerializer.Serialize(r, JsonFormat.Options)));
    public ValueTask AppendAsync(string messageId, InboundMessage message, CancellationToken token = default)
    {
        lock (gate) Transaction(() => InsertRaw(messageId, message.ReceivedAt, message.Envelope, message.Payload), token);
        return ValueTask.CompletedTask;
    }
    private void InsertRaw(string messageId, string receivedAt, Envelope envelope, byte[] payload, long? id = null)
    {
        var json = JsonSerializer.Serialize(envelope, JsonFormat.Options);
        var bytes = checked(payload.Length + Encoding.UTF8.GetByteCount(json));
        if (bytes > 4 * 1024 * 1024) throw new ArgumentException("Raw message too large");
        Charge("raw", bytes);
        Execute("INSERT INTO raw(id,message_id,received_at,envelope,payload,bytes) VALUES($id,$m,$r,$e,$p,$b)",
            ("$id", id), ("$m", messageId), ("$r", receivedAt), ("$e", json), ("$p", payload), ("$b", bytes));
    }
    public IReadOnlyList<RawMessage> Read(long afterId = 0, int limit = 100)
    {
        if (afterId < 0 || limit is < 1 or > 1000) throw new ArgumentException("Invalid raw query");
        lock (gate)
        {
            using var c = Command("SELECT id,message_id,received_at,envelope,payload FROM raw WHERE id>$id ORDER BY id LIMIT $limit", ("$id", afterId), ("$limit", limit));
            using var r = c.ExecuteReader(); var rows = new List<RawMessage>();
            while (r.Read()) rows.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2),
                JsonSerializer.Deserialize<Envelope>(r.GetString(3), JsonFormat.Options)!, (byte[])r[4]));
            return rows;
        }
    }
    public Dictionary<string, string> ListViews(string user)
    {
        lock (gate)
        {
            using var c = Command("SELECT name,definition FROM views WHERE user=$u ORDER BY name", ("$u", user)); using var r = c.ExecuteReader();
            var result = new Dictionary<string, string>(); while (r.Read()) result.Add(r.GetString(0), r.GetString(1)); return result;
        }
    }
    public void PutView(string user, string name, string definition)
    { lock (gate) Transaction(() => InsertView(user, name, definition)); }
    private void InsertView(string user, string name, string definition)
    {
        if (!ContractNames.Workspace(user) || !ContractNames.Workspace(name)) throw new ArgumentException("Invalid view owner/name");
        using var document = JsonDocument.Parse(definition);
        Execute("INSERT INTO views VALUES($u,$n,$d) ON CONFLICT(user,name) DO UPDATE SET definition=excluded.definition", ("$u", user), ("$n", name), ("$d", definition));
        if (Convert.ToInt64(Scalar("SELECT count(*) FROM views WHERE user=$u", ("$u", user))) > 128 ||
            Convert.ToInt64(Scalar("SELECT coalesce(sum(length(cast(definition as blob))),0) FROM views")) > 4 * 1024 * 1024)
            throw new IOException("View quota exceeded");
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; db.Dispose(); owner.Dispose(); disposed = true; }
    }
}
