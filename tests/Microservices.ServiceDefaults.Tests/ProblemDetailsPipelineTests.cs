using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microservices.ServiceDefaults.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microservices.ServiceDefaults.Tests;

public sealed class ProblemDetailsPipelineTests
{
    [Fact]
    public async Task ValidationExceptionProducesStableProblemContract()
    {
        using var services = CreateServices();
        var context = CreateContext(services);
        var handler = new ValidationExceptionHandler();
        var exception = new ValidationException(
        [
            new ValidationFailure("Email", "Email is required.")
        ]);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        var problem = await ReadProblemAsync(context);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("request.validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal("/errors/v1/platform/request.validation_failed", problem.GetProperty("type").GetString());
        Assert.Equal("Email is required.", problem.GetProperty("errors").GetProperty("Email")[0].GetString());
    }

    [Fact]
    public async Task UnsupportedAcceptFallsBackToProblemJson()
    {
        using var services = CreateServices();
        var context = CreateContext(services, "application/xml");
        var handler = new ValidationExceptionHandler();
        var exception = new ValidationException(
        [
            new ValidationFailure("Name", "Name is required.")
        ]);

        await handler.TryHandleAsync(context, exception, CancellationToken.None);
        var problem = await ReadProblemAsync(context);

        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("request.validation_failed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnhandledExceptionProducesSafeGenericProblem()
    {
        using var services = CreateServices();
        var context = CreateContext(services);
        var logger = services.GetRequiredService<ILogger<UnhandledExceptionHandler>>();
        var handler = new UnhandledExceptionHandler(logger);
        const string diagnosticSecret = "database-password-was-here";

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException(diagnosticSecret),
            CancellationToken.None);
        var body = await ReadBodyAsync(context);
        using var document = JsonDocument.Parse(body);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("server.unexpected", document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(diagnosticSecret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbortedRequestCancellationIsConsumedWithoutServerErrorBody()
    {
        using var services = CreateServices();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = CreateContext(services);
        context.RequestAborted = cancellation.Token;
        var logger = services.GetRequiredService<ILogger<UnhandledExceptionHandler>>();
        var handler = new UnhandledExceptionHandler(logger);

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(cancellation.Token),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task BodylessFrameworkStatusProducesProblemDetails(int statusCode)
    {
        using var services = CreateServices();
        var context = CreateContext(services);
        context.Response.StatusCode = statusCode;

        await PlatformProblemDetailsWriter.WriteStatusCodeAsync(context);
        var problem = await ReadProblemAsync(context);

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(statusCode, problem.GetProperty("status").GetInt32());
        Assert.Equal($"http.status.{statusCode}", problem.GetProperty("code").GetString());
        Assert.Equal($"/errors/v1/platform/http.status.{statusCode}", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public void NonCanonicalStatusCatalogCodeIsRejected()
    {
        Assert.False(PlatformProblemCatalog.TryResolve(
            "http.status.0503",
            out _));
    }

    private static ServiceProvider CreateServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddMicroserviceProblemDetails()
            .BuildServiceProvider();

    private static DefaultHttpContext CreateContext(
        IServiceProvider services,
        string accept = "application/problem+json")
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "test-trace"
        };
        context.Request.Path = "/test";
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpContext context)
    {
        var body = await ReadBodyAsync(context);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
