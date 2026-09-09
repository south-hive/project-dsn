using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.View;

public sealed record ViewDefinition(string[] Workspaces, string[] Fields);
public sealed class ViewService(IRecordQuery records)
{
    public void Validate(ViewDefinition view)
    {
        if (view.Workspaces is null || view.Fields is null || view.Workspaces.Length is < 1 or > 32 || view.Workspaces.Any(w => !ContractNames.Workspace(w)) ||
            view.Fields.Length is < 1 or > 32 || view.Fields.Any(f => !ContractNames.Field(f))) throw new ArgumentException("Invalid View definition");
        var catalog = records.Fields();
        var known = view.Workspaces.Where(catalog.ContainsKey).SelectMany(w => catalog[w]).Concat(["id", "workspace"]).ToHashSet();
        if (view.Fields.Any(f => !known.Contains(f))) throw new ArgumentException("Unknown field; inspect /fields first");
    }
    public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Read(ViewDefinition view, long afterId = 0, int limit = 100)
    {
        Validate(view);
        return records.Query(new(view.Workspaces, afterId, limit)).Select(row => (IReadOnlyDictionary<string, JsonElement>)view.Fields.Distinct().ToDictionary(f => f,
            f => f == "id" ? JsonSerializer.SerializeToElement(row.Id) : f == "workspace" ? JsonSerializer.SerializeToElement(row.Workspace) :
            row.Fields.TryGetValue(f, out var value) ? value : JsonSerializer.SerializeToElement<object?>(null))).ToArray();
    }
}
public sealed class ViewDefinitions
{
    private readonly object gate = new();
    private readonly string path;
    private Dictionary<string, Dictionary<string, ViewDefinition>> data;
    public ViewDefinitions(string path)
    {
        this.path = path;
        data = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ViewDefinition>>>(File.ReadAllText(path), JsonFormat.Options) ?? throw new InvalidDataException("Invalid views") : [];
    }
    public Dictionary<string, ViewDefinition> List(string user)
    {
        lock (gate) return data.TryGetValue(user, out var views) ? views.ToDictionary(p => p.Key, p => Copy(p.Value)) : [];
    }
    private static ViewDefinition Copy(ViewDefinition v) => new(v.Workspaces.ToArray(), v.Fields.ToArray());
    public void Put(string user, string name, ViewDefinition view)
    {
        if (!ContractNames.Workspace(name)) throw new ArgumentException("Invalid View name");
        lock (gate)
        {
            var next = data.ToDictionary(p => p.Key, p => new Dictionary<string, ViewDefinition>(p.Value));
            if (!next.TryGetValue(user, out var views)) next[user] = views = [];
            if (!views.ContainsKey(name) && views.Count >= 128) throw new ArgumentException("View limit reached");
            views[name] = Copy(view);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonFormat.Options);
            if (bytes.Length > 4 * 1024 * 1024) throw new ArgumentException("View storage quota exceeded");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, true); data = next;
        }
    }
}
