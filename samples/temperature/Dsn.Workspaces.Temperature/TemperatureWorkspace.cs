using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Workspaces.Temperature;

public sealed class TemperaturePlugin : IWorkspacePlugin
{
    public int ApiVersion => 1;
    public IWorkspace Create(WorkspaceServices services) => new TemperatureWorkspace(services.Records);
}

public sealed class TemperatureWorkspace(IRecordStore records) : IWorkspace
{
    public string Name => "temperature";

    public async ValueTask ProcessAsync(IMessageContext message, CancellationToken cancellationToken)
    {
        var lease = message.Checkout();
        RecordInput record;
        try
        {
            using var document = JsonDocument.Parse(lease.Payload.Copy());
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != "temperature.v1" ||
                !value.TryGetProperty("sensor", out var sensor) || sensor.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sensor.GetString()) || sensor.GetString()!.Length > 64 ||
                !value.TryGetProperty("celsius", out var celsius) || celsius.ValueKind != JsonValueKind.Number || !celsius.TryGetDouble(out var degrees) ||
                !double.IsFinite(degrees) || degrees is < -273.15 or > 1000 ||
                !value.TryGetProperty("sequence", out var sequence) || sequence.ValueKind != JsonValueKind.Number || !sequence.TryGetInt32(out var number) || number < 0)
                throw new FormatException("Expected temperature.v1 payload");
            record = new(Name, Fields.From(("message_id", lease.MessageId), ("source_id", lease.Envelope.SourceId),
                ("time", lease.Envelope.Time), ("schema", "temperature.v1"), ("sensor", sensor.GetString()),
                ("celsius", degrees), ("sequence", number)));
        }
        finally { message.Checkin(lease); }
        // All values are independent of the lease/document before storage can await.
        await records.AppendAsync(record, cancellationToken);
    }
}
