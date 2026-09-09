using System.Text.Json;
using System.Text.RegularExpressions;
using Dsn.Contracts;

namespace Dsn.Ingress;

public sealed class ProtocolException(string code, bool close = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool Close { get; } = close;
}
public interface IMessageDecoder { InboundMessage Decode(JsonElement value); }
public sealed class Version1Decoder(int maxPayloadBytes = 16384) : IMessageDecoder
{
    private readonly int maxPayload = maxPayloadBytes >= 0 ? maxPayloadBytes : throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
    public InboundMessage Decode(JsonElement p)
    {
        try
        {
            var source = Text(p, "source_id", 128);
            var time = Text(p, "time", 40);
            if (!Regex.IsMatch(time, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new FormatException();
            var kind = Text(p, "event_type", 64);
            var targets = p.GetProperty("workspace").EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (targets.Length is < 1 or > 32 || targets.Any(x => !ContractNames.Workspace(x))) throw new FormatException();
            var encoded = p.GetProperty("payload").GetString() ?? throw new FormatException();
            if (encoded.Length > ((long)maxPayload + 2) / 3 * 4) throw new FormatException();
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > maxPayload || Convert.ToBase64String(bytes) != encoded) throw new FormatException();
            JsonElement? description = p.TryGetProperty("source_description", out var d) ? d.Clone() : null;
            return new(new(1, source, description, time, kind, Array.AsReadOnly(targets)), bytes);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new ProtocolException("INVALID_ENVELOPE"); }
    }
    private static string Text(JsonElement p, string key, int max)
    {
        var s = p.GetProperty(key).GetString();
        return !string.IsNullOrWhiteSpace(s) && s.Length <= max ? s : throw new FormatException();
    }
}
public sealed class NotificationProtocol
{
    private readonly IReadOnlyDictionary<int, IMessageDecoder> decoders;
    public NotificationProtocol(int maxPayloadBytes = 16384) : this(new Dictionary<int, IMessageDecoder> { [1] = new Version1Decoder(maxPayloadBytes) }) { }
    public NotificationProtocol(IReadOnlyDictionary<int, IMessageDecoder> decoders) => this.decoders = new Dictionary<int, IMessageDecoder>(decoders);
    public InboundMessage Decode(ReadOnlyMemory<byte> frame)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw new ProtocolException("INVALID_RPC", true); }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0" ||
                !r.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String || method.GetString() != "dsn.publish" || r.TryGetProperty("id", out _) ||
                !r.TryGetProperty("params", out var p) || p.ValueKind != JsonValueKind.Object) throw new ProtocolException("INVALID_RPC", true);
            if (!p.TryGetProperty("version", out var v) || !v.TryGetInt32Safe(out var number) || !decoders.TryGetValue(number, out var decoder))
                throw new ProtocolException("UNSUPPORTED_VERSION");
            return decoder.Decode(p);
        }
    }
}
internal static class JsonExtensions
{
    internal static bool TryGetInt32Safe(this JsonElement e, out int n) { n = 0; return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out n); }
}
