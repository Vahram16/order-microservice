using Identity.Api.Features.Accounts.ConfirmingEmail.V1;

namespace Identity.Api.Tests;

public sealed class IdentitySliceValidationTests
{
    [Fact]
    public async Task ConfirmEmailRejectsMissingIdentityAndCode()
    {
        var validator = new ConfirmEmailCommandValidator();

        var result = await validator.ValidateAsync(
            new ConfirmEmailCommand(Guid.Empty, string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ConfirmEmailCommand.UserId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(ConfirmEmailCommand.Code));
    }

    [Fact]
    public async Task ResendConfirmationRequiresAValidEmailAddress()
    {
        var validator = new ResendEmailConfirmationCommandValidator();

        var result = await validator.ValidateAsync(
            new ResendEmailConfirmationCommand("not-an-email"));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(ResendEmailConfirmationCommand.Email));
    }
}
