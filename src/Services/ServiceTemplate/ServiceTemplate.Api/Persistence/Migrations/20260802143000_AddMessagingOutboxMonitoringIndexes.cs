using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceTemplate.Api.Persistence.Migrations;

[DbContext(typeof(ServiceTemplateDbContext))]
[Migration("20260802143000_AddMessagingOutboxMonitoringIndexes")]
public partial class AddMessagingOutboxMonitoringIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL cannot execute CREATE INDEX CONCURRENTLY inside a transaction. The migrator
        // runs before API rollout, and suppressTransaction prevents long write-blocking locks on a
        // populated OutboxMessage table. A failed concurrent build may leave an invalid index; the
        // deployment runbook requires dropping or REINDEXing it before rerunning this migration.
        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY "IX_OutboxMessage_BusPending_SentTime"
                ON "OutboxMessage" ("SentTime", "OutboxId")
                WHERE "OutboxId" IS NOT NULL;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            CREATE INDEX CONCURRENTLY "IX_OutboxMessage_ConsumerPending_SentTime"
                ON "OutboxMessage" ("SentTime", "InboxMessageId", "InboxConsumerId")
                WHERE "OutboxId" IS NULL
                  AND "InboxMessageId" IS NOT NULL
                  AND "InboxConsumerId" IS NOT NULL;
            """,
            suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX CONCURRENTLY "IX_OutboxMessage_BusPending_SentTime";
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            DROP INDEX CONCURRENTLY "IX_OutboxMessage_ConsumerPending_SentTime";
            """,
            suppressTransaction: true);
    }
}
