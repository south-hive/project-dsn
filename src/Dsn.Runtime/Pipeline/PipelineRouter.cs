using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Runtime;

/// <summary>Workspace output -> ordered filters -> sequential sink fan-out.
/// One emitted record is the processing unit; sink fan-out is not a distributed transaction.</summary>
public sealed class PipelineRouter : IRecordStore, IFilterRegistration
{
    internal static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    { "message_id", "processing_id", "replay_id", "source_id", "time", "event_type", "processor_version", "pipeline_revision", "record_id", "node_id" };
    private readonly Dictionary<string, Func<JsonElement, IRecordFilter>> factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IRecordStore> sinks;
    private readonly Dictionary<string, Plan> plans = new(StringComparer.Ordinal);
    private readonly IErrorSink errors;
    private readonly object configurationGate = new();
    private bool configured;
    private long received, dropped, completed, failed;
    public object Statistics => new { received = Interlocked.Read(ref received), dropped = Interlocked.Read(ref dropped), completed = Interlocked.Read(ref completed), failed = Interlocked.Read(ref failed) };
    public IReadOnlyDictionary<string, string> Revisions => new ReadOnlyDictionary<string, string>(plans.ToDictionary(p => p.Key, p => p.Value.Revision));
    private sealed record Plan(IRecordFilter[] Filters, IRecordStore[] Sinks, string Revision);
    public PipelineRouter(IRecordStore storage, IErrorSink errors, IRecordStore? console = null)
    {
        this.errors = errors;
        sinks = new(StringComparer.Ordinal) { ["sqlite"] = storage, ["discard"] = new DiscardSink() };
        if (console is not null) sinks.Add("console", console);
        foreach (var type in new[] { "where", "scale", "set", "select" })
        { var name = type; factories.Add(name, options => BuiltinFilters.Create(name, options)); }
    }
    public bool RegisterFilter(string type, Func<JsonElement, IRecordFilter> factory)
    {
        lock (configurationGate)
        {
            if (configured) throw new InvalidOperationException("Pipeline configuration is closed");
            if (!ContractNames.Workspace(type) || factory is null) return false;
            return factories.TryAdd(type, factory);
        }
    }
    public void Configure(IReadOnlyDictionary<string, PipelineDefinition> definitions, IEnumerable<string> workspaces)
    {
        lock (configurationGate)
        {
            if (configured) throw new InvalidOperationException("Pipeline already configured");
            var known = workspaces.ToHashSet(StringComparer.Ordinal);
            if (definitions.Count > 128 || definitions.Keys.Any(w => !known.Contains(w))) throw new ArgumentException("Pipeline must name a registered Workspace");
            var built = new Dictionary<string, Plan>();
            foreach (var name in known)
            {
                var definition = definitions.TryGetValue(name, out var found) ? found : new([], ["sqlite"]);
                if (definition is null || definition.Filters is null || definition.Filters.Length > 32 || definition.Sinks is null ||
                    definition.Sinks.Length is < 1 or > 8 || definition.Sinks.Distinct().Count() != definition.Sinks.Length || definition.Sinks.Any(s => s is null || !sinks.ContainsKey(s)))
                    throw new ArgumentException("Invalid pipeline filters/sinks");
                var filters = definition.Filters.Select(f =>
                {
                    if (f is null || f.Type is null || !factories.TryGetValue(f.Type, out var factory)) throw new ArgumentException("Unknown filter type");
                    return factory(f.Options.Clone()) ?? throw new ArgumentException("Null filter");
                }).ToArray();
                var fingerprint = JsonSerializer.Serialize(definition) + string.Join(';', filters.Select(f => f.GetType().AssemblyQualifiedName));
                var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))).ToLowerInvariant();
                built.Add(name, new(filters, definition.Sinks.Select(s => sinks[s]).ToArray(), revision));
            }
            foreach (var p in built) plans.Add(p.Key, p.Value);
            configured = true;
        }
    }
    public async ValueTask AppendAsync(RecordInput input, CancellationToken cancellationToken = default)
    {
        if (!configured) throw new InvalidOperationException("Pipeline not configured");
        if (!plans.TryGetValue(input.Workspace, out var plan)) throw new ArgumentException("No pipeline for Workspace");
        Interlocked.Increment(ref received);
        try
        {
            var record = Freeze(input);
            var provenance = record.Fields.Where(p => Protected.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Clone());
            provenance["pipeline_revision"] = JsonSerializer.SerializeToElement(plan.Revision);
            RecordInput Restore(RecordInput next)
            {
                var fields = next.Fields.Where(p => !Protected.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Clone());
                foreach (var p in provenance) fields[p.Key] = p.Value;
                return Freeze(new(input.Workspace, fields));
            }
            record = Restore(record);
            foreach (var filter in plan.Filters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = await filter.ProcessAsync(record, cancellationToken);
                if (next is null) { Interlocked.Increment(ref dropped); return; }
                if (next.Workspace != input.Workspace) throw new InvalidOperationException("Filter cannot change Workspace scope");
                record = Restore(Freeze(next));
            }
            // Filters cannot forge/drop the provenance produced by the decoder/runtime.
            var values = record.Fields.Where(p => !Protected.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Clone());
            foreach (var p in provenance) values[p.Key] = p.Value;
            record = Freeze(new(input.Workspace, values));
            foreach (var sink in plan.Sinks) { cancellationToken.ThrowIfCancellationRequested(); await sink.AppendAsync(record, cancellationToken); }
            Interlocked.Increment(ref completed);
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref failed);
            errors.Report(new("PIPELINE_FAILED", "pipeline", Workspace: input.Workspace, Detail: e.GetType().Name));
            throw;
        }
    }
    private static RecordInput Freeze(RecordInput record)
    {
        if (!ContractNames.Workspace(record.Workspace) || record.Fields.Count is < 1 or > 128 || record.Fields.Any(p => !ContractNames.Field(p.Key) || p.Key is "id" or "workspace" ||
            p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)))
            throw new ArgumentException("Pipeline records require 1..128 scalar fields");
        if (JsonSerializer.SerializeToUtf8Bytes(record.Fields).Length > 1024 * 1024) throw new ArgumentException("Pipeline record too large");
        return new(record.Workspace, new ReadOnlyDictionary<string, JsonElement>(record.Fields.ToDictionary(p => p.Key, p => p.Value.Clone())));
    }
    private sealed class DiscardSink : IRecordStore
    { public ValueTask AppendAsync(RecordInput record, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; } }
}
