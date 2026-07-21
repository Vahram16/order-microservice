using Microservices.Application;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed record ResendEmailConfirmationCommand(string Email) : ICommand;
