namespace Identity.Api.Features.Sessions.SigningIn;

public enum LoginOutcome
{
    Succeeded,
    RequiresTwoFactor,
    Failed
}
