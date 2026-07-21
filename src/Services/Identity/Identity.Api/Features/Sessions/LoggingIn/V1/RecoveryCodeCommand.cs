using MediatR;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed record RecoveryCodeCommand(string Code)
    : IRequest<LoginOutcome>;
