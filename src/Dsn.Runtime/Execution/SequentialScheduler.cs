namespace Dsn.Runtime;

// Deliberately sequential-only. This is not a public pluggable parallel-executor contract.
// Completion means every started invocation has returned, including its context cleanup.
internal sealed class SequentialScheduler
{
    internal async Task RunAsync(IEnumerable<WorkspaceInvocation> invocations, CancellationToken token)
    {
        using var iterator = invocations.GetEnumerator();
        while (!token.IsCancellationRequested && iterator.MoveNext())
            await iterator.Current.InvokeAsync(token);
    }
}
