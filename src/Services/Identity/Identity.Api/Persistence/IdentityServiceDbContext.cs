using Identity.Api.Model;
using Identity.Api.Notifications;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Persistence;

public sealed class IdentityServiceDbContext(
    DbContextOptions<IdentityServiceDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options),
        IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<IdentityNotificationOutboxMessage> NotificationOutbox =>
        Set<IdentityNotificationOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema("identity");

        builder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("users");
            user.Property(candidate => candidate.DisplayName).HasMaxLength(100);
            user.Property(candidate => candidate.CreatedAtUtc);
            user.HasIndex(candidate => candidate.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("ux_users_normalized_email");
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
        builder.Entity<IdentityNotificationOutboxMessage>(message =>
        {
            message.ToTable("notification_outbox");
            message.HasKey(candidate => candidate.Id);
            message.Property(candidate => candidate.DeduplicationKey).HasMaxLength(128);
            message.Property(candidate => candidate.ProtectedPayload).HasMaxLength(8192);
            message.Property(candidate => candidate.LastError).HasMaxLength(256);
            message.HasIndex(candidate => candidate.DeduplicationKey).IsUnique();
            message.HasIndex(candidate => new
            {
                candidate.ProcessedAtUtc,
                candidate.DeadLetteredAtUtc,
                candidate.AvailableAtUtc,
                candidate.LockedUntilUtc
            });
        });
    }
}
