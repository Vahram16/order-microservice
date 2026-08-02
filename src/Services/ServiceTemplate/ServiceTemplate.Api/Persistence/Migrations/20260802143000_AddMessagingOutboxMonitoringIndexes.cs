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
        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_BusPending_SentTime",
            table: "OutboxMessage",
            columns: new[] { "SentTime", "OutboxId" },
            filter: "\"OutboxId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessage_ConsumerPending_SentTime",
            table: "OutboxMessage",
            columns: new[] { "SentTime", "InboxMessageId", "InboxConsumerId" },
            filter: "\"OutboxId\" IS NULL AND \"InboxMessageId\" IS NOT NULL AND \"InboxConsumerId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessage_BusPending_SentTime",
            table: "OutboxMessage");

        migrationBuilder.DropIndex(
            name: "IX_OutboxMessage_ConsumerPending_SentTime",
            table: "OutboxMessage");
    }
}
