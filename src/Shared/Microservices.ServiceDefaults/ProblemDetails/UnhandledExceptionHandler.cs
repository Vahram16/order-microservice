using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microservices.ServiceDefaults.ProblemDetails;

internal sealed partial class UnhandledExceptionHandler(
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequestStatusCode = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException &&
            httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = ClientClosedRequestStatusCode;
            return true;
        }

        LogUnhandledException(logger, exception, httpContext.TraceIdentifier);

        var descriptor = PlatformProblemCatalog.Unexpected;
        var problem = PlatformProblemDetailsWriter.Create(httpContext, descriptor);
        await PlatformProblemDetailsWriter.WriteAsync(
            httpContext,
            problem,
            exception,
            cancellationToken);
        return true;
    }

    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Error,
        Message = "Unhandled request exception. Trace identifier: {TraceIdentifier}")]
    private static partial void LogUnhandledException(
        ILogger logger,
        Exception exception,
        string traceIdentifier);
}
