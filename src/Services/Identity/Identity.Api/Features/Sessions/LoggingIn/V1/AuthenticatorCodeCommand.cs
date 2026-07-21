using MediatR;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed record AuthenticatorCodeCommand(string Code)
    : IRequest<LoginOutcome>;
