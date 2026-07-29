using System.Net;
using System.Net.Http.Json;
using Customer.Api.Domain;
using Customer.Api.Features.Customers.AddingAddress.V1;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Features.Customers.UpdatingDetails.V1;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Tests;

public sealed class CustomerApiIntegrationTests(CustomerApiFactory factory)
    : IClassFixture<CustomerApiFactory>, IAsyncLifetime
{
    private const string Subject = "integration-subject";
    private static readonly string[] AllScopes =
    [
        "customers.self.read",
        "customers.self.update",
        "customers.addresses.write",
        "customers.self.export",
        "customers.self.delete"
    ];

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProvisioningIsConcurrentAndIdempotent()
    {
        await factory.ResetAsync();
        using var firstClient = factory.CreateAuthenticatedClient(Subject, AllScopes);
        using var secondClient = factory.CreateAuthenticatedClient(Subject, AllScopes);

        var responses = await Task.WhenAll(
            firstClient.PutAsync("/api/v1/customers/me", null),
            secondClient.PutAsync("/api/v1/customers/me", null));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response =>
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));

        await using var dbContext = await factory.CreateDbContextAsync();
        Assert.Equal(1, await dbContext.Customers.CountAsync());
    }

    [Fact]
    public async Task MutationsRequireCurrentStrongEtagAndAuthorizationScope()
    {
        await factory.ResetAsync();
        using var updateOnlyClient = factory.CreateAuthenticatedClient(
            Subject,
            "customers.self.update");
        var provision = await updateOnlyClient.PutAsync("/api/v1/customers/me", null);
        Assert.Equal(HttpStatusCode.Created, provision.StatusCode);
        var initialEtag = Assert.Single(provision.Headers.GetValues("ETag"));

        var forbiddenRead = await updateOnlyClient.GetAsync("/api/v1/customers/me");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);

        var missingPrecondition = await updateOnlyClient.PutAsJsonAsync(
            "/api/v1/customers/me/details",
            new UpdateCustomerDetailsRequest("Grace", "Hopper", "grace@example.com", null));
        Assert.Equal((HttpStatusCode)428, missingPrecondition.StatusCode);

        using var update = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/customers/me/details")
        {
            Content = JsonContent.Create(
                new UpdateCustomerDetailsRequest(
                    "Grace",
                    "Hopper",
                    "grace@example.com",
                    null))
        };
        update.Headers.TryAddWithoutValidation("If-Match", initialEtag);
        var updated = await updateOnlyClient.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var currentEtag = Assert.Single(updated.Headers.GetValues("ETag"));
        Assert.NotEqual(initialEtag, currentEtag);

        using var stale = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/customers/me/details")
        {
            Content = JsonContent.Create(
                new UpdateCustomerDetailsRequest(
                    "Stale",
                    "Writer",
                    "stale@example.com",
                    null))
        };
        stale.Headers.TryAddWithoutValidation("If-Match", initialEtag);
        var staleResponse = await updateOnlyClient.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
    }

    [Fact]
    public async Task DefaultAddressSwitchAndIdempotentRetryArePersistedCorrectly()
    {
        await factory.ResetAsync();
        using var client = factory.CreateAuthenticatedClient(Subject, AllScopes);
        var provision = await client.PutAsync("/api/v1/customers/me", null);
        var versionOne = Assert.Single(provision.Headers.GetValues("ETag"));

        var firstKey = Guid.NewGuid();
        var first = await AddAddressAsync(client, versionOne, firstKey, "Home");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var versionTwo = Assert.Single(first.Headers.GetValues("ETag"));

        var secondKey = Guid.NewGuid();
        var second = await AddAddressAsync(client, versionTwo, secondKey, "Office");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var retry = await AddAddressAsync(client, versionTwo, secondKey, "Office");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        await using var dbContext = await factory.CreateDbContextAsync();
        var addresses = await dbContext.CustomerAddresses
            .AsNoTracking()
            .OrderBy(address => address.Label)
            .ToArrayAsync();
        Assert.Equal(2, addresses.Length);
        Assert.Single(addresses, address => address.IsDefaultShipping);
        Assert.True(addresses.Single(address => address.Id == secondKey).IsDefaultShipping);
    }

    [Fact]
    public async Task AccountClosureAnonymizesPiiRemovesAddressesAndWritesAudit()
    {
        await factory.ResetAsync();
        using var client = factory.CreateAuthenticatedClient(Subject, AllScopes);
        var provision = await client.PutAsync("/api/v1/customers/me", null);
        var versionOne = Assert.Single(provision.Headers.GetValues("ETag"));
        var address = await AddAddressAsync(client, versionOne, Guid.NewGuid(), "Home");
        var versionTwo = Assert.Single(address.Headers.GetValues("ETag"));

        using var close = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/customers/me");
        close.Headers.TryAddWithoutValidation("If-Match", versionTwo);
        var closed = await client.SendAsync(close);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var response = await closed.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(response);
        Assert.Equal(CustomerStatus.Deactivated.ToString(), response.Status);
        Assert.Null(response.FirstName);
        Assert.Null(response.LastName);
        Assert.Null(response.Email);
        Assert.Empty(response.Addresses);

        await using var dbContext = await factory.CreateDbContextAsync();
        var customer = await dbContext.Customers
            .Include(entity => entity.Addresses)
            .SingleAsync();
        Assert.Equal(CustomerStatus.Deactivated, customer.Status);
        Assert.Null(customer.Email);
        Assert.Empty(customer.Addresses);
        Assert.Contains(
            await dbContext.CustomerAuditEntries.Select(entry => entry.Action).ToArrayAsync(),
            action => action == "customer.account_closed");
    }

    private static async Task<HttpResponseMessage> AddAddressAsync(
        HttpClient client,
        string etag,
        Guid idempotencyKey,
        string label)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/customers/me/addresses")
        {
            Content = JsonContent.Create(new AddCustomerAddressRequest(
                label,
                "Ada Lovelace",
                "12 Computing Street",
                null,
                "London",
                null,
                "SW1A 1AA",
                "GB",
                "+44 20 0000 0000",
                true,
                false))
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
        return await client.SendAsync(request);
    }
}
