using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Workspaces.Examples;

// A sample schema, not a DSN-wide model. Other workspaces may accept binary or different JSON.
public sealed class BenchPlugin : IWorkspacePlugin
{
    public int ApiVersion => 1;
    public IWorkspace Create(WorkspaceServices services) => new BenchWorkspace();
}
public sealed class BenchWorkspace : IWorkspace
{
    public string Name => "bench";
    public async ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken)
    {
        var lease = message.Checkout();
        Dictionary<string, JsonElement> result;
        try
        {
            using var document = JsonDocument.Parse(lease.Payload.Copy());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) || schema.GetString() != "bench.v1")
                throw new FormatException("Expected bench.v1");
            result = new();
            foreach (var key in new[] { "schema", "pc_id", "dut_id", "run_id", "instance_id", "role" })
            {
                var value = root.GetProperty(key);
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 128)
                    throw new FormatException("Invalid bench identity");
                result.Add(key, value.Clone());
            }
            var sequence = root.GetProperty("sequence");
            if (!sequence.TryGetInt64(out var number) || number < 0) throw new FormatException("Invalid sequence");
            result.Add("sequence", sequence.Clone());
            var values = root.GetProperty("values");
            if (values.ValueKind != JsonValueKind.Object) throw new FormatException("Expected scalar values");
            foreach (var p in values.EnumerateObject())
            {
                var name = "value_" + p.Name;
                if (result.Count >= 96 || !ContractNames.Field(name) || p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    throw new FormatException("Invalid bench value");
                result.Add(name, p.Value.Clone());
            }
        }
        finally { message.Checkin(lease); }
        // No database/file dependency; Runtime adds provenance and persists results.
        await message.EmitAsync(result, cancellationToken);
    }
}
