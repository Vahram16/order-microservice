namespace Identity.Api.Features.Sessions.SigningIn.Password.V1;

public sealed record LoginRequest(string Email, string Password);
