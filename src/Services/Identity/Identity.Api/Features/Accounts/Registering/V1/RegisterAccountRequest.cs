namespace Identity.Api.Features.Accounts.Registering.V1;

public sealed record RegisterAccountRequest(
    string Email,
    string Password,
    string DisplayName);
