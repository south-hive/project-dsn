using Dsn.Contracts;

namespace Dsn.Runtime;

// Registration is serialized by DsnRuntime and closed before the processing loop starts.
internal sealed class WorkspaceRegistry(IErrorSink errors, IRecordStore? results = null)
{
    private readonly Dictionary<string, IWorkspace> items = new(StringComparer.Ordinal);
    internal string[] Names => items.Keys.Order().ToArray();
    internal bool Register(IWorkspace workspace)
    {
        if (!ContractNames.Workspace(workspace.Name) || !items.TryAdd(workspace.Name, workspace))
        {
            errors.Report(new("REGISTRATION_REJECTED", "registry", Workspace: workspace.Name));
            return false;
        }
        return true;
    }
    internal IEnumerable<WorkspaceInvocation> Resolve(OwnedMessage message)
    {
        foreach (var name in message.Envelope.Workspace.Distinct(StringComparer.Ordinal))
        {
            if (items.TryGetValue(name, out var workspace)) yield return new(message, workspace, errors, results);
            else errors.Report(new("UNKNOWN_WORKSPACE", "runtime", message.Envelope.SourceId, name));
        }
    }
}
