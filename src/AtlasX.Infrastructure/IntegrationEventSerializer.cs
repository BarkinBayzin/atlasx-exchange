using System.Text.Json;

namespace AtlasX.Infrastructure;

public static class IntegrationEventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, Type> KnownTypes = BuildTypeMap();

    public static string Serialize(IIntegrationEvent integrationEvent)
    {
        if (integrationEvent is null)
        {
            throw new ArgumentNullException(nameof(integrationEvent));
        }

        return JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions);
    }

    public static IIntegrationEvent Deserialize(string typeName, string payload)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Type name is required.", nameof(typeName));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload is required.", nameof(payload));
        }

        if (!KnownTypes.TryGetValue(typeName, out var type))
        {
            throw new InvalidOperationException($"Unknown integration event type '{typeName}'.");
        }

        var result = JsonSerializer.Deserialize(payload, type, JsonOptions);
        if (result is not IIntegrationEvent integrationEvent)
        {
            throw new InvalidOperationException($"Failed to deserialize integration event '{typeName}'.");
        }

        return integrationEvent;
    }

    private static IReadOnlyDictionary<string, Type> BuildTypeMap()
    {
        var types = typeof(IIntegrationEvent).Assembly
            .GetTypes()
            .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            .ToDictionary(type => type.Name, type => type, StringComparer.Ordinal);

        return types;
    }
}
