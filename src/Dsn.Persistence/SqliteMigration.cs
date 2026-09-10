using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Persistence;

public sealed partial class SqliteStore
{
    /// <summary>Transactional, one-time import. Legacy files are read exclusively and never edited/deleted.</summary>
    public void ImportLegacy(string directory)
    {
        lock (gate)
        {
            if (Scalar("SELECT value FROM metadata WHERE key='legacy_imported'") is not null) return;
            Transaction(() =>
            {
                if (Convert.ToInt64(Scalar("SELECT count(*) FROM records")) != 0 || Convert.ToInt64(Scalar("SELECT count(*) FROM raw")) != 0)
                    throw new InvalidDataException("Legacy import requires an empty database");
                long expected = 1;
                foreach (var row in LegacyRows(Path.Combine(directory, "records.ndjson")))
                {
                    if (row.Id != expected++) throw new InvalidDataException("Invalid legacy record sequence");
                    var frozen = RecordValidation.Freeze(row); InsertRecord(new(frozen.Workspace, frozen.Fields), row.Id);
                }
                expected = 1;
                foreach (var row in LegacyRows(Path.Combine(directory, "raw.ndjson")))
                {
                    if (row.Id != expected++ || row.Workspace != "raw") throw new InvalidDataException("Invalid legacy raw sequence");
                    InsertRaw(row.Fields["message_id"].GetString()!, row.Fields["received_at"].GetString()!,
                        JsonSerializer.Deserialize<Envelope>(row.Fields["envelope"].GetString()!, JsonFormat.Options)!,
                        Convert.FromBase64String(row.Fields["payload"].GetString()!), row.Id);
                }
                var viewsPath = Path.Combine(directory, "views.json");
                if (File.Exists(viewsPath))
                {
                    using var file = new FileStream(viewsPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Legacy views too large");
                    var views = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(file, JsonFormat.Options)
                        ?? throw new InvalidDataException("Null legacy views");
                    foreach (var user in views) foreach (var view in user.Value) InsertView(user.Key, view.Key, view.Value.GetRawText());
                }
                Execute("INSERT INTO metadata VALUES('legacy_imported','1')");
            });
        }
    }
    private static IEnumerable<StoredRecord> LegacyRows(string path)
    {
        if (!File.Exists(path)) yield break;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        if (file.Length == 0) yield break;
        // Previous journal commits a row only when LF was written. An incomplete tail stays untouched.
        file.Position = file.Length - 1; bool completeTail = file.ReadByte() == 10; file.Position = 0;
        using var reader = new StreamReader(file);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (reader.EndOfStream && !completeTail) yield break;
            if (line.Length > 1024 * 1024) throw new InvalidDataException("Legacy row too large");
            yield return JsonSerializer.Deserialize<StoredRecord>(line, JsonFormat.Options) ?? throw new InvalidDataException("Null legacy row");
        }
    }
}
