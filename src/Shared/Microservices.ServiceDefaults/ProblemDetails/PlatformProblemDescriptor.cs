namespace Microservices.ServiceDefaults.ProblemDetails;

public sealed record PlatformProblemDescriptor(
    string Code,
    string Title,
    int Status,
    string Description,
    bool Retryable)
{
    public string Type => $"/errors/v1/platform/{Code}";
}
