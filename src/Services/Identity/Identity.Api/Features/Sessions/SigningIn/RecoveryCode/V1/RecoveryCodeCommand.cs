using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1;

public sealed record RecoveryCodeCommand(string Code)
    : IRequest<LoginOutcome>;
