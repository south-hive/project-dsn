using System.Text;
using System.Text.Json;
using Dsn.Contracts;

sealed class DelegateWorkspace(string name, Func<IMessageContext, CancellationToken, ValueTask> action) : IWorkspace
{
    public string Name => name;
    public ValueTask ProcessAsync(IMessageContext message, CancellationToken token) => action(message, token);
}
sealed class BlockingStore : IRecordStore
{
    public readonly TaskCompletionSource Entered = new(), Release = new();
    public async ValueTask AppendAsync(RecordInput input, CancellationToken cancellationToken = default)
    { Entered.SetResult(); await Release.Task; Suite.Equal("hello", input.Fields["payload_utf8"].GetString()); }
}
sealed class Suite
{
    private int passed, failed;
    private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsn-tests-" + Guid.NewGuid().ToString("N"));
    public string Path(string name) => System.IO.Path.Combine(root, name);
    public async Task Test(string name, Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(30)); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e}"); }
    }
    public void Finish() { Console.WriteLine($"RESULT passed={passed} failed={failed}"); Environment.ExitCode = failed == 0 ? 0 : 1; if (Directory.Exists(root)) Directory.Delete(root, true); }
    public static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    public static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task Eventually(Func<bool> condition) { using var timeout = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(10, timeout.Token); }
    public static byte[] Frame(string source, string[] workspace, byte[] payload) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "dsn.publish", @params = new {
        version = 1, source_id = source, time = "2026-09-08T12:00:00Z", event_type = "normal", workspace, payload = Convert.ToBase64String(payload) } }) + "\n");
    public static InboundMessage Message(string source, string[] workspace, byte[] payload) => new(new Envelope(1, source, null, "2026-09-08T12:00:00Z", "normal", Array.AsReadOnly(workspace)), payload);
}

sealed class CollectingSink(System.Collections.Concurrent.ConcurrentQueue<InboundMessage>? messages = null) : IMessageSink
{
    public bool TrySubmit(InboundMessage message) { messages?.Enqueue(message); return true; }
}
