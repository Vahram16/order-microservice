using System.Security.Claims;
using Payment.Api.Features.PaymentMethods.Common;

namespace Payment.Api.Infrastructure;

internal sealed record CurrentPaymentIdentity(string Provider, string Subject)
{
    private const string KeycloakProvider = "keycloak";
    private const int MaximumSubjectLength = 255;
    public static Result<CurrentPaymentIdentity> From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject)) return PaymentApplicationErrors.AuthenticationRequired;
        if (subject.Length > MaximumSubjectLength || !string.Equals(subject, subject.Trim(), StringComparison.Ordinal)) return PaymentApplicationErrors.InvalidIdentityClaims;
        return Result.Success(new CurrentPaymentIdentity(KeycloakProvider, subject));
    }
}
