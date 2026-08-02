using System.Text.Json;
using MassTransit;
using Microservices.Contracts;
using Microservices.Messaging;

namespace Microservices.Messaging.Tests;

public sealed class IntegrationMessageIdentityTests
{
    [Fact]
    public void CanonicalContractDoesNotRequireTransportMetadataInPayload()
    {
        var propertyNames = typeof(IIntegrationMessage)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Empty(propertyNames);
    }

    [Fact]
    public void HistoricalPayloadWithUnknownAdditiveFieldStillDeserializes()
    {
        const string historicalPayload =
            """
            {
              "orderId": "8a625563-23d6-4f89-9708-a98bb5070cc5",
              "status": "accepted",
              "occurredAtUtc": "2026-07-31T12:34:56+00:00",
              "retiredOptionalField": "ignored"
            }
            """;

        var message = JsonSerializer.Deserialize<OrderAcceptedV1>(
            historicalPayload,
            IntegrationContractJson.CreateOptions());

        Assert.NotNull(message);
        Assert.Equal(Guid.Parse("8a625563-23d6-4f89-9708-a98bb5070cc5"), message.OrderId);
        Assert.Equal("accepted", message.Status);
    }

    [Fact]
    public void BreakingContractVersionUsesDistinctMessageIdentity()
    {
        var versionOne = MessageUrn.ForType<Contracts.V1.OrderAccepted>();
        var versionTwo = MessageUrn.ForType<Contracts.V2.OrderAccepted>();

        Assert.NotEqual(versionOne, versionTwo);
    }

    [Fact]
    public void ApplicationHeadersCannotOverrideTransportIdentity()
    {
        var headers = new Dictionary<string, string>
        {
            [IntegrationTransportHeaders.CausationId] = Guid.NewGuid().ToString()
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            IntegrationTransportHeaders.ValidateApplicationHeaders(headers));

        Assert.Contains("transport-owned", exception.Message, StringComparison.Ordinal);
    }

    private sealed record OrderAcceptedV1(
        Guid OrderId,
        string Status,
        DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
}

namespace Microservices.Messaging.Tests.Contracts.V1
{
    internal sealed record OrderAccepted(Guid OrderId, DateTimeOffset OccurredAtUtc)
        : IIntegrationEvent;
}

namespace Microservices.Messaging.Tests.Contracts.V2
{
    internal sealed record OrderAccepted(Guid OrderId, string Currency, DateTimeOffset OccurredAtUtc)
        : IIntegrationEvent;
}
