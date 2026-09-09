using System.Collections.ObjectModel;
using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Persistence;

internal static class RecordValidation
{
    internal static StoredRecord Freeze(StoredRecord row)
    {
        if (!ContractNames.Workspace(row.Workspace) || row.Fields.Count is < 1 or > 128) throw new ArgumentException("Invalid record");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in row.Fields)
        {
            if (!ContractNames.Field(name) || name is "id" or "workspace" || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                throw new ArgumentException("Record fields must be named JSON scalars; id/workspace are reserved");
            fields.Add(name, value.Clone());
        }
        return row with { Fields = new ReadOnlyDictionary<string, JsonElement>(fields) };
    }
    internal static void Validate(RecordQuery query)
    {
        if (query.AfterId < 0 || query.Limit is < 1 or > 1000 || query.Workspaces is { } w && (w.Length is < 1 or > 32 || w.Any(x => !ContractNames.Workspace(x)))) throw new ArgumentException("Invalid query");
    }
}
