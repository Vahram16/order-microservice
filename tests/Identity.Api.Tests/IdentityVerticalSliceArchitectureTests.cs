using Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1;
using Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1;
using Identity.Api.Features.Accounts.PasswordRecovery.RequestReset.V1;
using Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1;
using Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;
using Identity.Api.Features.Sessions.SigningIn.Password.V1;
using Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1;

namespace Identity.Api.Tests;

public sealed class IdentityVerticalSliceArchitectureTests
{
    public static TheoryData<Type, string> OperationNamespaces => new()
    {
        {
            typeof(ConfirmEmailCommand),
            "Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1"
        },
        {
            typeof(ResendEmailConfirmationCommand),
            "Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1"
        },
        {
            typeof(RequestPasswordResetCommand),
            "Identity.Api.Features.Accounts.PasswordRecovery.RequestReset.V1"
        },
        {
            typeof(ResetPasswordCommand),
            "Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1"
        },
        {
            typeof(LoginCommand),
            "Identity.Api.Features.Sessions.SigningIn.Password.V1"
        },
        {
            typeof(AuthenticatorCodeCommand),
            "Identity.Api.Features.Sessions.SigningIn.Authenticator.V1"
        },
        {
            typeof(RecoveryCodeCommand),
            "Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1"
        }
    };

    [Theory]
    [MemberData(nameof(OperationNamespaces))]
    public void EachOperationHasItsOwnVerticalSliceNamespace(
        Type operationType,
        string expectedNamespace) =>
        Assert.Equal(expectedNamespace, operationType.Namespace);
}
