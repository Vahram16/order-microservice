using Microservices.Contracts;

namespace Microservices.Contracts.Products.V1;

public sealed record ProductCatalogChanged(
    Guid ProductId,
    string Sku,
    string Name,
    decimal Price,
    string CurrencyCode,
    long Version,
    bool IsAvailable,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
