using System.Net;
using System.Net.Sockets;
using Dsn.Contracts;

namespace Dsn.Ingress;

public sealed class TcpNotificationReceiver(NotificationProtocol protocol, IMessageSink sink, IErrorSink errors,
    int maxSessions = 128, int maxFrameBytes = 65536) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<TcpClient, string?> clients = [];
    private readonly Dictionary<string, TcpClient> sources = new(StringComparer.Ordinal);
    private readonly HashSet<Task> sessions = [];
    private readonly CancellationTokenSource stop = new();
    private TcpListener? listener;
    private Task? accepting;
    public int Port => ((IPEndPoint)listener!.LocalEndpoint).Port;
    public int SessionCount { get { lock (gate) return clients.Count; } }
    public void Start(string host, int port)
    {
        if (listener is not null || maxSessions < 1 || maxFrameBytes < 1) throw new InvalidOperationException("Invalid RPC configuration/state");
        listener = new(IPAddress.Parse(host), port); listener.Start(); accepting = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(stop.Token);
                lock (gate)
                {
                    if (clients.Count >= maxSessions) { errors.Report(new("SESSION_LIMIT", "rpc")); client.Dispose(); continue; }
                    clients.Add(client, null);
                    var task = Task.Run(() => SessionAsync(client)); sessions.Add(task);
                    _ = task.ContinueWith(t => { lock (gate) sessions.Remove(t); }, TaskScheduler.Default);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task SessionAsync(TcpClient client)
    {
        string? source = null;
        var frame = new byte[maxFrameBytes]; var used = 0; var input = new byte[8192];
        try
        {
            var stream = client.GetStream();
            int n;
            while ((n = await stream.ReadAsync(input, stop.Token)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    if (input[i] != 10)
                    {
                        if (used == frame.Length) throw new ProtocolException("FRAME_TOO_LARGE", true);
                        frame[used++] = input[i]; continue;
                    }
                    InboundMessage message;
                    try { message = protocol.Decode(frame.AsMemory(0, used)); }
                    catch (ProtocolException e) { errors.Report(new(e.Code, "rpc", source)); if (e.Close) return; continue; }
                    finally { used = 0; }
                    lock (gate)
                    {
                        if (source is not null && source != message.Envelope.SourceId) throw new ProtocolException("SOURCE_CHANGED", true);
                        if (source is null)
                        {
                            if (sources.ContainsKey(message.Envelope.SourceId)) throw new ProtocolException("SOURCE_IN_USE", true);
                            source = message.Envelope.SourceId; sources.Add(source, client); clients[client] = source;
                        }
                    }
                    sink.TrySubmit(message);
                }
            }
            if (used > 0) errors.Report(new("TRUNCATED_FRAME", "rpc", source));
        }
        catch (ProtocolException e) { errors.Report(new(e.Code, "rpc", source)); }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        { if (!stop.IsCancellationRequested) errors.Report(new("SESSION_CLOSED", "rpc", source)); }
        catch (Exception e) { errors.Report(new("INGRESS_FAILED", "rpc", source, Detail: e.GetType().Name)); }
        finally { lock (gate) { clients.Remove(client); if (source is not null) sources.Remove(source); } client.Dispose(); }
    }
    public void DisconnectSource(string sourceId)
    {
        lock (gate) if (sources.TryGetValue(sourceId, out var client)) client.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener?.Stop();
        if (accepting is not null) await accepting;
        Task[] tasks;
        lock (gate) { foreach (var client in clients.Keys) client.Dispose(); tasks = sessions.ToArray(); }
        await Task.WhenAll(tasks); stop.Dispose();
    }
}
