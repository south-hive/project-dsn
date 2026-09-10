using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Workspaces.Examples;

public sealed class DemoPlugin : IWorkspacePlugin
{
    public int ApiVersion => 1;
    public IWorkspace Create(WorkspaceServices services) => new DemoWorkspace();
}

// Synthetic application telemetry only. Thresholds illustrate processing, not defect diagnosis.
public sealed class DemoWorkspace : IWorkspace
{
    public string Name => "telemetry-demo";
    public async ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken)
    {
        var lease = message.Checkout();
        Dictionary<string, JsonElement> fields;
        try
        {
            using var document = JsonDocument.Parse(lease.Payload.Copy());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Expected demo object");
            string Text(string key)
            {
                var v = root.GetProperty(key);
                if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()) || v.GetString()!.Length > 128)
                    throw new FormatException("Invalid demo identity");
                return v.GetString()!;
            }
            long Number(string key, long max = 1000000000)
            {
                var value = root.GetProperty(key);
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var v) || v < 0 || v > max) throw new FormatException("Invalid demo measurement");
                return v;
            }
            if (Text("schema") != "dsn-demo.v1") throw new FormatException("Expected dsn-demo.v1");
            var phase = Text("phase");
            if (phase is not ("normal" or "latency" or "errors" or "recovery")) throw new FormatException("Invalid demo phase");
            var interval = Number("interval_ms", 60000);
            if (interval == 0) throw new FormatException("Interval must be positive");
            var latency = Number("latency_us"); var errors = Number("io_errors");
            var calls = Number("api_calls");
            fields = Fields.From(("pc_id", Text("pc_id")), ("dut_id", Text("dut_id")), ("run_id", Text("run_id")),
                ("sequence", Number("sequence")), ("phase", phase), ("interval_ms", interval),
                ("latency_us", latency), ("read_iops", Number("read_iops")), ("write_iops", Number("write_iops")),
                ("api_calls", calls), ("api_calls_per_sec", calls * 1000.0 / interval), ("io_errors", errors),
                ("assessment", errors > 0 ? "error" : latency >= 500 ? "warning" : "normal"))
                .ToDictionary(p => p.Key, p => p.Value);
        }
        finally { message.Checkin(lease); }
        await message.EmitAsync(fields, cancellationToken);
    }
}
