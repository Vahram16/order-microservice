using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Api.Persistence.Migrations;

[DbContext(typeof(NotificationDbContext))]
[Migration("20260727000000_InitialNotifications")]
public partial class InitialNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "notifications");

        migrationBuilder.CreateTable(
            name: "data_protection_keys",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                FriendlyName = table.Column<string>(type: "text", nullable: true),
                Xml = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_data_protection_keys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "deliveries",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LockId = table.Column<Guid>(type: "uuid", nullable: true),
                LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AcceptedByProviderAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deliveries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_dispatch",
            schema: "notifications",
            table: "deliveries",
            columns: new[] { "AcceptedByProviderAtUtc", "DeadLetteredAtUtc", "AvailableAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ux_deliveries_idempotency_key",
            schema: "notifications",
            table: "deliveries",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_deliveries_source_event",
            schema: "notifications",
            table: "deliveries",
            columns: new[] { "Source", "SourceEventId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "data_protection_keys", schema: "notifications");
        migrationBuilder.DropTable(name: "deliveries", schema: "notifications");
    }
}
