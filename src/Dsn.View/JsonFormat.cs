using System.Text.Json;

namespace Dsn.View;

internal static class JsonFormat
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
