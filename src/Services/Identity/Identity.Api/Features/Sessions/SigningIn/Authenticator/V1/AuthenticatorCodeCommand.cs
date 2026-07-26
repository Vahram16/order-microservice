using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;

public sealed record AuthenticatorCodeCommand(string Code)
    : IRequest<LoginOutcome>;
