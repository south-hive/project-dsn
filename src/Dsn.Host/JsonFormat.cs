using System.Text.Json;

namespace Dsn.Host;

internal static class JsonFormat
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
