using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.Password.V1;

public sealed record LoginCommand(
    string Email,
    string Password) : IRequest<LoginOutcome>;
