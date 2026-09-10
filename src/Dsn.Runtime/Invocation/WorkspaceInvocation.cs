using Dsn.Contracts;

namespace Dsn.Runtime;

// Owns exactly one call context. No scheduler may close its leases before ProcessAsync completes.
internal sealed class WorkspaceInvocation(OwnedMessage message, IWorkspace workspace, IErrorSink errors, IRecordStore? results = null)
{
    private int invoked;
    internal async Task InvokeAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref invoked, 1) != 0) throw new InvalidOperationException("Invocation already started");
        using var context = new MessageContext(message, workspace.Name, results, workspace.GetType().Assembly.GetName().Version?.ToString());
        try { await workspace.ProcessAsync(context, token); }
        catch (Exception e) { errors.Report(new("WORKSPACE_FAILED", "runtime", message.Envelope.SourceId, workspace.Name, e.GetType().Name)); }
    }
}
