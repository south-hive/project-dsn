using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Test fixture producer only. Awaited network writes are deliberately not a production Source SDK.
if (args is ["--help"]) { Console.WriteLine("Usage: dotnet Dsn.TestSource.dll [port=7070] [text=hello] [count=1] [host=127.0.0.1]"); return; }
var port = args.Length > 0 ? int.Parse(args[0]) : 7070;
var text = args.Length > 1 ? args[1] : "hello";
var count = args.Length > 2 ? int.Parse(args[2]) : 1;
var host = args.Length > 3 ? args[3] : "127.0.0.1";
if (count < 1 || count > 1000000) throw new ArgumentOutOfRangeException(nameof(count));
using var client = new TcpClient();
await client.ConnectAsync(host, port);
for (var i = 0; i < count; i++)
{
    var frame = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", method = "dsn.publish", @params = new {
        version = 1, source_id = "test-source", time = DateTimeOffset.UtcNow.ToString("O"), event_type = "normal", workspace = new[] { "echo", "hex" }, payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) } });
    await client.GetStream().WriteAsync(frame); await client.GetStream().WriteAsync(new byte[] { 10 });
}
Console.WriteLine($"Sent {count} test notifications; no delivery acknowledgement.");
