using System.Security.Cryptography;
using Identity.Api.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Api.Infrastructure;

internal sealed class DummyPasswordVerifier
{
    private readonly PasswordHasher<ApplicationUser> _hasher;
    private readonly string _passwordHash;
    private readonly ApplicationUser _user = new();

    public DummyPasswordVerifier(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<ApplicationUser>(options);
        _passwordHash = _hasher.HashPassword(
            _user,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public void Verify(string password) =>
        _hasher.VerifyHashedPassword(_user, _passwordHash, password);
}
