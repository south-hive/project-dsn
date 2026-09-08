using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Dsn.Core;

if (args is ["--help"]) { Console.WriteLine("Usage: dotnet Dsn.Mock.dll [port=7072] [bind=127.0.0.1]"); return; }
var port = args.Length > 0 ? int.Parse(args[0]) : 7072;
var bind = args.Length > 1 ? args[1] : "127.0.0.1";
var errors = new ErrorSink();
var output = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
long dropped = 0;
var writer = Task.Run(async () => { await foreach (var line in output.Reader.ReadAllAsync()) await Console.Out.WriteLineAsync(line); });
await using (var rpc = new RpcServer(new Protocol(), m =>
{
    var line = JsonSerializer.Serialize(new { envelope = m.Envelope, payloadHex = Convert.ToHexString(m.Payload.AsSpan(0, Math.Min(256, m.Payload.Length))).ToLowerInvariant(), bytes = m.Payload.Length, truncated = m.Payload.Length > 256 }, Json.Options);
    if (!output.Writer.TryWrite(line)) Interlocked.Increment(ref dropped);
}, errors))
{
    rpc.Start(bind, port);
    Console.Error.WriteLine($"DSN Mock ready on {bind}:{rpc.Port}");
    var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    using var signal = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
    await stop.Task;
}
output.Writer.Complete();
if (await Task.WhenAny(writer, Task.Delay(1000)) == writer) await writer;
Console.Error.WriteLine(JsonSerializer.Serialize(new { errors = errors.Snapshot(), dropped }, Json.Options));
