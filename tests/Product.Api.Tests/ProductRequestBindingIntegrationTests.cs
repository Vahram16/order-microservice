using System.Net;
using System.Net.Http.Json;
using System.Text;
using Product.Api.Features.Products.Common;
using Product.Api.Features.Products.Creating.V1;

namespace Product.Api.Tests;

public sealed class ProductRequestBindingIntegrationTests(ProductApiFactory factory)
    : IClassFixture<ProductApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OmittedCreatePriceIsRejectedWithoutCreatingProduct()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var content = JsonContentWithoutPrice(
            "BOOK-001",
            "Domain-Driven Design",
            null,
            "USD");

        var response = await client.PostAsync("/api/v1/products", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listed = await client.GetFromJsonAsync<ProductListResponse>("/api/v1/products");
        Assert.NotNull(listed);
        Assert.Empty(listed.Items);
        Assert.Equal(0, listed.TotalCount);
    }

    [Fact]
    public async Task OmittedUpdatePriceIsRejectedWithoutMutatingProduct()
    {
        using var client = factory.CreateAuthenticatedClient();
        var created = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest(
                "BOOK-001",
                "Domain-Driven Design",
                "Original description",
                49.99m,
                "USD"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        var etag = Assert.Single(created.Headers.GetValues("ETag"));

        using var update = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/products/{product.Id}")
        {
            Content = JsonContentWithoutPrice(
                "BOOK-002",
                "Changed name",
                "Changed description",
                "EUR")
        };
        update.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fetched = await client.GetFromJsonAsync<ProductResponse>(
            $"/api/v1/products/{product.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("BOOK-001", fetched.Sku);
        Assert.Equal("Domain-Driven Design", fetched.Name);
        Assert.Equal("Original description", fetched.Description);
        Assert.Equal(49.99m, fetched.Price);
        Assert.Equal("USD", fetched.CurrencyCode);
        Assert.Equal(1, fetched.Version);
    }

    [Fact]
    public async Task ExplicitZeroPriceRemainsValid()
    {
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest(
                "FREE-001",
                "Free product",
                null,
                0m,
                "USD"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(0m, product.Price);
    }

    private static StringContent JsonContentWithoutPrice(
        string sku,
        string name,
        string? description,
        string currencyCode)
    {
        var descriptionJson = description is null
            ? "null"
            : $"\"{description}\"";
        var json = $$"""
            {
              "sku": "{{sku}}",
              "name": "{{name}}",
              "description": {{descriptionJson}},
              "currencyCode": "{{currencyCode}}"
            }
            """;
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
