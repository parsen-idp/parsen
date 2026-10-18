using System.Globalization;
using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Parsen.Application.Dossiers;

namespace Parsen.Infrastructure.Events;

/// <param name="EventBusName">Barramento próprio da Parsen (parsen-{stage}), criado na stack Pipeline.</param>
public sealed record EventBridgeSettings(string EventBusName);

/// <summary>
/// Contrato do evento DossierSubmitted com o pipeline. O EventBridge roteia por Source e DetailType;
/// o Detail leva só IDs (regra 3). Mudar um campo aqui exige mudar quem consome.
/// </summary>
public static class DossierSubmittedEvent
{
    public const string Source = "parsen.api";
    public const string DetailType = "DossierSubmitted";

    public static string ToDetailJson(DossierSubmitted submitted)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tenant_id", submitted.TenantId.Value);
            writer.WriteString("dossier_id", submitted.DossierId.ToString());
            writer.WriteStartArray("document_ids");
            foreach (var documentId in submitted.DocumentIds)
            {
                writer.WriteStringValue(documentId.ToString());
            }

            writer.WriteEndArray();
            writer.WriteString("submitted_at", submitted.SubmittedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>Publica eventos de integração no barramento da Parsen.</summary>
public sealed class EventBridgePublisher(IAmazonEventBridge eventBridge, EventBridgeSettings settings) : IEventPublisher
{
    public async Task PublishAsync(DossierSubmitted submitted, CancellationToken cancellationToken)
    {
        var response = await eventBridge.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = settings.EventBusName,
                    Source = DossierSubmittedEvent.Source,
                    DetailType = DossierSubmittedEvent.DetailType,
                    Detail = DossierSubmittedEvent.ToDetailJson(submitted),
                },
            ],
        }, cancellationToken);

        // O PutEvents responde 200 mesmo quando uma entrada é recusada: o erro vem por entrada
        if (response.FailedEntryCount is > 0)
        {
            var entry = response.Entries?.FirstOrDefault(e => e.ErrorCode is not null);
            throw new InvalidOperationException(
                $"O EventBridge recusou o evento DossierSubmitted: {entry?.ErrorCode} {entry?.ErrorMessage}");
        }
    }
}
