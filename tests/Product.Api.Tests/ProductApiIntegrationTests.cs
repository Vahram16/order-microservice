using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Product.Api.Features.Products.Common;
using Product.Api.Features.Products.Creating.V1;
using Product.Api.Features.Products.Updating.V1;

namespace Product.Api.Tests;

public sealed class ProductApiIntegrationTests(ProductApiFactory factory)
    : IClassFixture<ProductApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProductEndpointsRequireAuthentication()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/v1/products?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CrudFlowUsesStrongEtagsAndBoundedListing()
    {
        using var client = factory.CreateAuthenticatedClient();
        var created = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest(
                " book-001 ",
                " Domain-Driven Design ",
                null,
                49.99m,
                " usd "));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal("BOOK-001", product.Sku);
        Assert.Equal("Domain-Driven Design", product.Name);
        Assert.Equal("USD", product.CurrencyCode);
        var versionOne = AssertStrongEtag(created, product.Id, 1);
        Assert.Equal($"/api/v1/products/{product.Id}", created.Headers.Location?.OriginalString);

        var fetched = await client.GetAsync($"/api/v1/products/{product.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        AssertStrongEtag(fetched, product.Id, 1);

        var listed = await client.GetFromJsonAsync<ProductListResponse>(
            "/api/v1/products");
        Assert.NotNull(listed);
        Assert.Equal(1, listed.Page);
        Assert.Equal(20, listed.PageSize);
        Assert.Equal(1, listed.TotalCount);
        Assert.Equal(product.Id, Assert.Single(listed.Items).Id);

        var missingPrecondition = await client.PutAsJsonAsync(
            $"/api/v1/products/{product.Id}",
            UpdatedRequest());
        await AssertProductProblemAsync(
            missingPrecondition,
            (HttpStatusCode)428,
            "product.precondition_required");

        using var crossProductEtag = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/products/{product.Id}")
        {
            Content = JsonContent.Create(UpdatedRequest())
        };
        crossProductEtag.Headers.TryAddWithoutValidation(
            "If-Match",
            $"\"product-{Guid.NewGuid():N}-1\"");
        var crossProductResponse = await client.SendAsync(crossProductEtag);
        await AssertProductProblemAsync(
            crossProductResponse,
            HttpStatusCode.BadRequest,
            "product.invalid_precondition");

        using var update = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/products/{product.Id}")
        {
            Content = JsonContent.Create(UpdatedRequest())
        };
        update.Headers.TryAddWithoutValidation("If-Match", versionOne);
        var updated = await client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var versionTwo = AssertStrongEtag(updated, product.Id, 2);

        using var stale = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/products/{product.Id}")
        {
            Content = JsonContent.Create(UpdatedRequest() with { Name = "Stale update" })
        };
        stale.Headers.TryAddWithoutValidation("If-Match", versionOne);
        var staleResponse = await client.SendAsync(stale);
        await AssertProductProblemAsync(
            staleResponse,
            HttpStatusCode.PreconditionFailed,
            "product.version_mismatch");

        using var delete = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/products/{product.Id}");
        delete.Headers.TryAddWithoutValidation("If-Match", versionTwo);
        var deleted = await client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var missing = await client.GetAsync($"/api/v1/products/{product.Id}");
        await AssertProductProblemAsync(
            missing,
            HttpStatusCode.NotFound,
            "product.not_found");
    }

    [Fact]
    public async Task DuplicateNormalizedSkuUsesStableConflictContract()
    {
        using var client = factory.CreateAuthenticatedClient();
        var first = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest("sku-001", "First", null, 10m, "USD"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest(" SKU-001 ", "Second", null, 20m, "USD"));

        await AssertProductProblemAsync(
            duplicate,
            HttpStatusCode.Conflict,
            "product.sku_conflict");
    }

    [Fact]
    public async Task UpdatingToAnExistingNormalizedSkuUsesStableConflictContract()
    {
        using var client = factory.CreateAuthenticatedClient();
        var first = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest("sku-001", "First", null, 10m, "USD"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/api/v1/products",
            new CreateProductRequest("sku-002", "Second", null, 20m, "USD"));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondProduct = await second.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(secondProduct);
        var secondEtag = AssertStrongEtag(second, secondProduct.Id, 1);

        using var update = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/products/{secondProduct.Id}")
        {
            Content = JsonContent.Create(new UpdateProductRequest(
                " SKU-001 ",
                "Second",
                null,
                20m,
                "USD"))
        };
        update.Headers.TryAddWithoutValidation("If-Match", secondEtag);

        var conflict = await client.SendAsync(update);

        await AssertProductProblemAsync(
            conflict,
            HttpStatusCode.Conflict,
            "product.sku_conflict");
    }

    private static UpdateProductRequest UpdatedRequest() =>
        new(
            "BOOK-002",
            "Implementing Domain-Driven Design",
            "Updated edition",
            59.95m,
            "EUR");

    private static string AssertStrongEtag(
        HttpResponseMessage response,
        Guid productId,
        long version)
    {
        var etag = Assert.Single(response.Headers.GetValues("ETag"));
        Assert.Equal($"\"product-{productId:N}-{version}\"", etag);
        Assert.False(etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase));
        return etag;
    }

    private static async Task AssertProductProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        Assert.StartsWith(
            "/errors/v1/product/",
            problem.GetProperty("type").GetString(),
            StringComparison.Ordinal);
    }
}
