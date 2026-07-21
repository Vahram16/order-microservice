using Identity.Api.Configuration;
using Identity.Api.Features.Authorization;
using Identity.Api.Model;
using Identity.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Api.Tests;

public sealed class IdentityInteractionSecurityTests
{
    private static readonly IServiceProvider TestServices =
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void LogoutInteractionIsBoundToTheExactCompletionUri()
    {
        var timeProvider = new TestTimeProvider(
            new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
        var protector = CreateLogoutProtector(timeProvider);
        const string completionUri =
            "/connect/logout?request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3Aone";

        var token = protector.Protect(completionUri);

        Assert.True(protector.IsValid(token, completionUri));
        Assert.False(protector.IsValid(
            token,
            "/connect/logout?request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3Atwo"));
        Assert.False(protector.IsValid(token + "tampered", completionUri));
    }

    [Fact]
    public void LogoutInteractionExpires()
    {
        var timeProvider = new TestTimeProvider(
            new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
        var protector = CreateLogoutProtector(timeProvider);
        const string completionUri = "/connect/logout?request_uri=opaque";
        var token = protector.Protect(completionUri);

        timeProvider.Advance(TimeSpan.FromMinutes(4));

        Assert.False(protector.IsValid(token, completionUri));
    }

    [Fact]
    public void InteractionUrlsRejectExternalReturnTargets()
    {
        var builder = new IdentityInteractionUrlBuilder(
            Options.Create(new IdentityInteractionOptions
            {
                PublicOrigin = "https://identity.example.com/"
            }));

        var uri = builder.CreateLoginUri("https://attacker.example/steal");

        Assert.Equal(
            "https://identity.example.com/account/login?returnUrl=%2F",
            uri);
    }

    [Fact]
    public async Task PasswordValidatorRejectsBlockedPasswords()
    {
        var validator = CreatePasswordValidator();
        var user = new ApplicationUser
        {
            Email = "customer@example.com",
            UserName = "customer@example.com",
            DisplayName = "Example Customer"
        };

        var result = await validator.ValidateAsync(
            CreateUserManager(),
            user,
            "correcthorsebatterystaple");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "PasswordBlocked");
    }

    [Fact]
    public async Task PasswordValidatorRejectsUserSpecificTerms()
    {
        var validator = CreatePasswordValidator();
        var user = new ApplicationUser
        {
            Email = "vahram@example.com",
            UserName = "vahram@example.com",
            DisplayName = "Vahram Customer"
        };

        var result = await validator.ValidateAsync(
            CreateUserManager(),
            user,
            "A-long-password-for-vahram-2026");

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Code == "PasswordContainsPersonalData");
    }

    private static LogoutInteractionProtector CreateLogoutProtector(
        TimeProvider timeProvider) =>
        new(
            new EphemeralDataProtectionProvider(),
            Options.Create(new IdentityInteractionOptions
            {
                PublicOrigin = "https://identity.example.com/",
                InteractionTokenLifetime = TimeSpan.FromMinutes(3)
            }),
            timeProvider);

    private static BlockedPasswordValidator CreatePasswordValidator()
    {
        var options = Options.Create(new IdentityPasswordPolicyOptions
        {
            RejectUserInputs = true
        });
        return new BlockedPasswordValidator(
            new PasswordBlocklist(options),
            options);
    }

    private static UserManager<ApplicationUser> CreateUserManager() =>
        new(
            new TestUserStore(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            TestServices,
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private sealed class TestUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString("D"));

        public Task<string?> GetUserNameAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(
            ApplicationUser user,
            string? userName,
            CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            ApplicationUser user,
            string? normalizedName,
            CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(
            ApplicationUser user,
            CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<ApplicationUser?> FindByIdAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationUser?>(null);
    }
}
