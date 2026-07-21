using System.Collections.Frozen;
using System.Text;
using Identity.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Identity.Api.Security;

internal sealed class PasswordBlocklist
{
    private static readonly string[] DevelopmentEntries =
    [
        "passwordpassword",
        "password123456",
        "qwertyqwertyqwerty",
        "letmeinletmeinletmein",
        "thisisnotasecurepassword",
        "correcthorsebatterystaple",
        "adminadminadminadmin",
        "welcome123456789",
        "changemechangeme",
        "iloveyouiloveyou",
        "trustno1trustno1",
        "footballfootball",
        "monkeymonkeymonkey",
        "dragonDragonDragon",
        "companynamepassword",
        "summer2026summer",
        "winter2026winter",
        "identityidentity",
        "bookingbookingbooking",
        "administratorpassword"
    ];

    private readonly FrozenSet<string> _entries;

    public PasswordBlocklist(
        IOptions<IdentityPasswordPolicyOptions> options)
    {
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in DevelopmentEntries)
        {
            entries.Add(Normalize(value));
        }

        if (!string.IsNullOrWhiteSpace(options.Value.BlocklistPath))
        {
            foreach (var line in File.ReadLines(options.Value.BlocklistPath))
            {
                var value = line.Trim();
                if (value.Length == 0 || value.StartsWith('#'))
                {
                    continue;
                }

                entries.Add(Normalize(value));
            }
        }

        _entries = entries.ToFrozenSet(StringComparer.Ordinal);
    }

    public bool Contains(string password) =>
        _entries.Contains(Normalize(password));

    public static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC)
            .Trim()
            .ToUpperInvariant();
}
