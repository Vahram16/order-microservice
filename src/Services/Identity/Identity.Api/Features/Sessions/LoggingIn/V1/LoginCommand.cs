using MediatR;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed record LoginCommand(
    string Email,
    string Password) : IRequest<LoginOutcome>;
