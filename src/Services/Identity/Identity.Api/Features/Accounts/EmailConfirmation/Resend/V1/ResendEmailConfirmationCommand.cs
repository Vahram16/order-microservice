using Microservices.Application;

namespace Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1;

public sealed record ResendEmailConfirmationCommand(string Email) : ICommand;
