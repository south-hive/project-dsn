using System.Runtime.InteropServices;
using System.Text.Json;
using Dsn.Host;

if (args.Length > 1 || args is ["--help"])
{
    Console.WriteLine("Usage: dotnet Dsn.Host.dll [settings.json]"); return;
}
try
{
    var settings = Settings.Load(args.FirstOrDefault());
    await using var host = new DsnApplication(settings);
    await host.StartAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { status = "ready", rpcPort = host.Ingress.Port, view = host.ViewAddress }, JsonFormat.Options));
    var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    using var signal = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
    await stop.Task;
}
catch (Exception e) { Console.Error.WriteLine($"DSN failed: {e.GetType().Name}: {e.Message}"); Environment.ExitCode = 1; }
