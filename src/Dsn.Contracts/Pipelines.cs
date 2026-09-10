using System.Text.Json;

namespace Dsn.Contracts;

/// <summary>One immutable scalar record in, zero or one record out. Null drops the record.
/// The runtime calls filters sequentially. Completion includes all work; detached work is forbidden.</summary>
public interface IRecordFilter
{
    ValueTask<RecordInput?> ProcessAsync(RecordInput record, CancellationToken token);
}
public interface IFilterPlugin
{
    int ApiVersion { get; }
    string Type { get; }
    IRecordFilter Create(JsonElement options);
}
public interface IFilterRegistration
{
    bool RegisterFilter(string type, Func<JsonElement, IRecordFilter> factory);
}
public sealed record FilterDefinition(string Type, JsonElement Options);
public sealed record PipelineDefinition(FilterDefinition[] Filters, string[] Sinks);

public interface ISavedViewStore
{
    Dictionary<string, string> ListViews(string user);
    void PutView(string user, string name, string definition);
}
