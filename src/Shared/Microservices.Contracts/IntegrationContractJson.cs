using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microservices.Contracts;

/// <summary>Canonical System.Text.Json settings for integration-contract compatibility tests and transport serialization.</summary>
public static class IntegrationContractJson
{
    public static JsonSerializerOptions CreateOptions() => Configure(new JsonSerializerOptions());

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.AllowTrailingCommas = false;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;

        return options;
    }
}
