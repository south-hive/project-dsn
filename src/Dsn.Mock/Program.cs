using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Dsn.Contracts;
using Dsn.Ingress;

if (args is ["--help"]) { Console.WriteLine("Usage: dotnet Dsn.Mock.dll [port=7072] [bind=127.0.0.1]"); return; }
var port = args.Length > 0 ? int.Parse(args[0]) : 7072;
var bind = args.Length > 1 ? args[1] : "127.0.0.1";
var errors = new MockDiagnostics();
var echo = new ConsoleEcho();
var writer = echo.WriteAsync();
await using (var receiver = new TcpNotificationReceiver(new NotificationProtocol(), echo, errors))
{
    receiver.Start(bind, port);
    Console.Error.WriteLine($"DSN Mock ready on {bind}:{receiver.Port}");
    var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    using var signal = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
    await stop.Task;
}
echo.Complete();
if (await Task.WhenAny(writer, Task.Delay(1000)) == writer) await writer;
Console.Error.WriteLine(JsonSerializer.Serialize(new { errors = errors.Count, lastError = errors.Last, dropped = echo.Dropped }));

internal sealed class ConsoleEcho : IMessageSink
{
    private readonly Channel<string> output = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private long dropped;
    public long Dropped => Interlocked.Read(ref dropped);
    public bool TrySubmit(InboundMessage message)
    {
        var line = JsonSerializer.Serialize(new { envelope = message.Envelope,
            payloadHex = Convert.ToHexString(message.Payload.AsSpan(0, Math.Min(256, message.Payload.Length))).ToLowerInvariant(),
            bytes = message.Payload.Length, truncated = message.Payload.Length > 256 }, json);
        if (output.Writer.TryWrite(line)) return true;
        Interlocked.Increment(ref dropped); return false;
    }
    public void Complete() => output.Writer.Complete();
    public async Task WriteAsync() { await foreach (var line in output.Reader.ReadAllAsync()) await Console.Out.WriteLineAsync(line); }
}
internal sealed class MockDiagnostics : IErrorSink
{
    private long count;
    private ErrorBody? last;
    public long Count => Interlocked.Read(ref count);
    public ErrorBody? Last => Volatile.Read(ref last);
    public void Report(ErrorBody body) { Volatile.Write(ref last, body); Interlocked.Increment(ref count); }
}
