using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Runtime;

internal static class BuiltinFilters
{
    private sealed class Filter(Func<RecordInput, RecordInput?> apply) : IRecordFilter
    {
        public ValueTask<RecordInput?> ProcessAsync(RecordInput record, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(apply(record)); }
    }
    internal static IRecordFilter Create(string type, JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object) throw new ArgumentException("Filter options must be an object");
        void Keys(params string[] names) { if (options.EnumerateObject().Any(p => !names.Contains(p.Name))) throw new ArgumentException("Unknown filter option"); }
        string Text(string key) => options.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new ArgumentException("Missing filter option " + key);
        string Field(string key, bool writable = false)
        {
            var name = Text(key);
            if (!ContractNames.Field(name) || name is "id" or "workspace" || writable && PipelineRouter.Protected.Contains(name)) throw new ArgumentException("Invalid/reserved filter field");
            return name;
        }
        static Dictionary<string, JsonElement> Copy(RecordInput r) => r.Fields.ToDictionary(p => p.Key, p => p.Value.Clone());
        if (type == "where")
        {
            Keys("field", "op", "value"); var field = Field("field"); var op = Text("op");
            if (op is not ("eq" or "ne" or "gt" or "gte" or "lt" or "lte" or "exists" or "not-exists")) throw new ArgumentException("Invalid where operator");
            var unary = op is "exists" or "not-exists";
            var expected = options.TryGetProperty("value", out var value) ? value.Clone() : default;
            if (!unary && expected.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) throw new ArgumentException("Expected scalar comparison value");
            if (op is "gt" or "gte" or "lt" or "lte" && !Number(expected, out _)) throw new ArgumentException("Numeric comparison requires a finite number");
            return new Filter(r =>
            {
                bool exists = r.Fields.TryGetValue(field, out var actual);
                if (unary) return (op == "exists" ? exists : !exists) ? r : null;
                if (!exists) return null;
                bool match;
                if (op is "eq" or "ne")
                {
                    bool equal = actual.ValueKind == expected.ValueKind && (actual.ValueKind == JsonValueKind.Number
                        ? Number(actual, out var a) && Number(expected, out var b) && a == b : actual.ToString() == expected.ToString());
                    match = op == "eq" ? equal : !equal;
                }
                else
                {
                    if (!Number(actual, out var a) || !Number(expected, out var b)) return null;
                    match = op switch { "gt" => a > b, "gte" => a >= b, "lt" => a < b, _ => a <= b };
                }
                return match ? r : null;
            });
        }
        if (type == "scale")
        {
            Keys("field", "output", "factor"); var field = Field("field"); var output = Field("output", true);
            if (!options.TryGetProperty("factor", out var factorElement) || !Number(factorElement, out var factor)) throw new ArgumentException("Invalid scale factor");
            return new Filter(r =>
            {
                if (!r.Fields.TryGetValue(field, out var value)) return r; // Heterogeneous sources may omit this measurement.
                if (!Number(value, out var n) || !double.IsFinite(n * factor)) throw new FormatException("Scale requires a finite number");
                var fields = Copy(r); fields[output] = JsonSerializer.SerializeToElement(n * factor); return new(r.Workspace, fields);
            });
        }
        if (type == "set")
        {
            Keys("field", "value"); var field = Field("field", true);
            if (!options.TryGetProperty("value", out var value) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) throw new ArgumentException("Set requires a scalar value");
            var frozen = value.Clone(); return new Filter(r => { var fields = Copy(r); fields[field] = frozen; return new(r.Workspace, fields); });
        }
        if (type == "select")
        {
            Keys("fields");
            if (!options.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) throw new ArgumentException("Select requires fields");
            var names = fields.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray();
            if (names.Length is < 1 or > 128 || names.Any(n => !ContractNames.Field(n) || n is "id" or "workspace")) throw new ArgumentException("Invalid select fields");
            return new Filter(r => new(r.Workspace, r.Fields.Where(p => names.Contains(p.Key) || PipelineRouter.Protected.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Clone())));
        }
        throw new ArgumentException("Unknown built-in filter");
    }
    private static bool Number(JsonElement value, out double number)
    { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number); }
}
