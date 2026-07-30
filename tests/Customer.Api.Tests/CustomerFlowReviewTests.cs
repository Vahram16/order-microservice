using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Customer.Api.Features.Customers.UpdatingDetails.V1;
using Microsoft.AspNetCore.Http;

namespace Customer.Api.Tests;

public sealed class CustomerFlowReviewTests(CustomerApiFactory factory)
    : IClassFixture<CustomerApiFactory>, IAsyncLifetime
{
    private const string Subject = "flow-review-subject";

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PlatformCatalogPreservesExplicitServiceUnavailableStatus()
    {
        using var client = factory.CreateClient();

        var descriptor = await client.GetFromJsonAsync<JsonElement>(
            "/errors/v1/platform/http.status.503");

        Assert.Equal("http.status.503", descriptor.GetProperty("code").GetString());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, descriptor.GetProperty("status").GetInt32());
        Assert.Equal(
            "/errors/v1/platform/http.status.503",
            descriptor.GetProperty("type").GetString());
        Assert.True(descriptor.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task MultipleIfMatchValuesAreInvalidRatherThanMissing()
    {
        await factory.ResetAsync();
        using var client = factory.CreateAuthenticatedClient(
            Subject,
            "customers.self.update");
        var provision = await client.PutAsync("/api/v1/customers/me", null);
        var etag = Assert.Single(provision.Headers.GetValues("ETag"));

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/customers/me/details")
        {
            Content = JsonContent.Create(new UpdateCustomerDetailsRequest(
                "Grace",
                "Hopper",
                "grace@example.com",
                null))
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            [etag, "\"customer-999\""]);

        var response = await client.SendAsync(request);

        await AssertCustomerProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "customer.invalid_precondition");
    }

    [Fact]
    public async Task ClosedAccountStillEnforcesCurrentEtag()
    {
        await factory.ResetAsync();
        using var client = factory.CreateAuthenticatedClient(
            Subject,
            "customers.self.update",
            "customers.self.delete");
        var provision = await client.PutAsync("/api/v1/customers/me", null);
        var activeEtag = Assert.Single(provision.Headers.GetValues("ETag"));

        using var close = CreateCloseRequest(activeEtag);
        var closed = await client.SendAsync(close);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedEtag = Assert.Single(closed.Headers.GetValues("ETag"));

        using var staleRetry = CreateCloseRequest(activeEtag);
        var stale = await client.SendAsync(staleRetry);
        await AssertCustomerProblemAsync(
            stale,
            HttpStatusCode.PreconditionFailed,
            "customer.version_mismatch");

        using var currentRetry = CreateCloseRequest(closedEtag);
        var current = await client.SendAsync(currentRetry);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(closedEtag, Assert.Single(current.Headers.GetValues("ETag")));
    }

    private static HttpRequestMessage CreateCloseRequest(string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/customers/me");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return request;
    }

    private static async Task AssertCustomerProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(
            "/errors/v1/customer/" + expectedCode,
            problem.GetProperty("type").GetString());
    }
}
