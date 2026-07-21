namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public enum LoginOutcome
{
    Succeeded,
    RequiresTwoFactor,
    Failed
}
