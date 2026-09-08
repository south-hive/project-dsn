using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsn.Core;

namespace Dsn.Host;

public sealed record UserAccess(string Token, string[] Workspaces);
public sealed class Settings
{
    public string Bind { get; init; } = "127.0.0.1";
    public int RpcPort { get; init; } = 7070;
    public int ViewPort { get; init; } = 7071;
    public string DataDirectory { get; init; } = "data";
    public string[] Plugins { get; init; } = [];
    public int QueueCapacity { get; init; } = 1024;
    public long QueueBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxMessages { get; init; } = 4096;
    public long LiveBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxSessions { get; init; } = 128;
    public int FrameBytes { get; init; } = 65536;
    public int PayloadBytes { get; init; } = 16384;
    public int ErrorCapacity { get; init; } = 1024;
    public int RecordCapacity { get; init; } = 100000;
    public long JournalBytes { get; init; } = 256 * 1024 * 1024;
    public int ShutdownSeconds { get; init; } = 5;
    public Dictionary<string, UserAccess> Users { get; init; } = [];
    public void Validate()
    {
        if (!IPAddress.TryParse(Bind, out var address) || RpcPort is < 0 or > 65535 || ViewPort is < 0 or > 65535 ||
            QueueCapacity < 1 || QueueBytes < 1 || MaxMessages < 1 || LiveBytes < 1 || MaxSessions < 1 || FrameBytes is < 1 or > 4 * 1024 * 1024 ||
            PayloadBytes < 0 || PayloadBytes > FrameBytes || ErrorCapacity < 1 || RecordCapacity < 1 || JournalBytes < 1 || ShutdownSeconds is < 1 or > 300 ||
            string.IsNullOrWhiteSpace(DataDirectory) || Plugins is null || Users is null) throw new ArgumentException("Invalid DSN settings");
        if (!IPAddress.IsLoopback(address) && Users.Count == 0) throw new ArgumentException("Remote View requires configured users/tokens");
        if (Users.Count > 128 || Users.Any(p => !Names.Valid(p.Key) || p.Value is null || string.IsNullOrWhiteSpace(p.Value.Token) || p.Value.Token.Length < 16 ||
            p.Value.Workspaces is null || p.Value.Workspaces.Length is < 1 or > 32 || p.Value.Workspaces.Any(w => !Names.Valid(w))) || Users.Values.Select(u => u.Token).Distinct().Count() != Users.Count)
            throw new ArgumentException("Invalid user configuration");
    }
    public static Settings Load(string? path)
    {
        var options = new JsonSerializerOptions(Json.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var settings = path is null ? new Settings() : JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), options) ?? throw new ArgumentException("Null settings");
        settings.Validate(); return settings;
    }
}
