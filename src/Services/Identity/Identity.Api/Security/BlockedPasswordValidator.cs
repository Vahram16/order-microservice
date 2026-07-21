using Identity.Api.Configuration;
using Identity.Api.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Api.Security;

internal sealed class BlockedPasswordValidator(
    PasswordBlocklist blocklist,
    IOptions<IdentityPasswordPolicyOptions> options)
    : IPasswordValidator<ApplicationUser>
{
    private static readonly char[] UserTermSeparators =
        ['@', '.', '_', '-', '+', ' ', '\t'];

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        if (blocklist.Contains(password))
        {
            return Task.FromResult(Failed(
                "PasswordBlocked",
                "Choose a password that is not commonly used or known to be compromised."));
        }

        if (options.Value.RejectUserInputs &&
            ContainsUserSpecificTerm(password, user))
        {
            return Task.FromResult(Failed(
                "PasswordContainsPersonalData",
                "Choose a password that does not contain your email, user name, or display name."));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    private static bool ContainsUserSpecificTerm(
        string password,
        ApplicationUser user)
    {
        var normalizedPassword = PasswordBlocklist.Normalize(password);
        foreach (var candidate in new[]
                 {
                     user.Email,
                     user.UserName,
                     user.DisplayName
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalizedCandidate = PasswordBlocklist.Normalize(candidate);
            if (normalizedCandidate.Length >= 4 &&
                normalizedPassword.Contains(
                    normalizedCandidate,
                    StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var term in candidate.Split(
                         UserTermSeparators,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                var normalizedTerm = PasswordBlocklist.Normalize(term);
                if (normalizedTerm.Length >= 4 &&
                    normalizedPassword.Contains(
                        normalizedTerm,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IdentityResult Failed(string code, string description) =>
        IdentityResult.Failed(new IdentityError
        {
            Code = code,
            Description = description
        });
}
