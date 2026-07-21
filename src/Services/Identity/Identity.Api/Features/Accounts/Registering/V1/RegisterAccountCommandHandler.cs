using System.Text;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using Identity.Api.Notifications;
using Identity.Api.Persistence;
using Microservices.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Api.Features.Accounts.Registering.V1;

internal sealed class RegisterAccountCommandHandler(
    UserManager<ApplicationUser> userManager,
    IIdentityNotificationSender notificationSender,
    IdentityServiceDbContext dbContext,
    DummyPasswordVerifier dummyPasswordVerifier,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterAccountCommand>
{
    public async Task Handle(
        RegisterAccountCommand command,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        var email = command.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            dummyPasswordVerifier.Verify(command.Password);
            await AccountEnumerationResistance.CompleteAsync(
                timeProvider,
                startedAt,
                cancellationToken);
            return;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                DisplayName = command.DisplayName.Trim(),
                CreatedAtUtc = timeProvider.GetUtcNow(),
                IsActive = true,
                LockoutEnabled = true
            };

            IdentityResult result;
            try
            {
                result = await userManager.CreateAsync(user, command.Password);
            }
            catch (DbUpdateException exception) when (IsDuplicateAccount(exception))
            {
                return;
            }

            if (!result.Succeeded)
            {
                if (result.Errors.All(error =>
                        error.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    return;
                }

                throw new IdentityOperationException(result.Errors);
            }

            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            await notificationSender.SendEmailConfirmationAsync(
                email,
                user.Id,
                encodedToken,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        await AccountEnumerationResistance.CompleteAsync(
            timeProvider,
            startedAt,
            cancellationToken);
    }

    private static bool IsDuplicateAccount(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_users_normalized_email" or "UserNameIndex"
        };
}
