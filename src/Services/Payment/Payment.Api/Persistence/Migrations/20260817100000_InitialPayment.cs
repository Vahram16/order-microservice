using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payment.Api.Persistence.Migrations;

[DbContext(typeof(PaymentDbContext))]
[Migration("20260817100000_InitialPayment")]
public partial class InitialPayment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InboxState",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<Guid>(type: "uuid", nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InboxState", x => x.Id);
                table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
            });

        migrationBuilder.CreateTable(
            name: "OutboxState",
            columns: table => new
            {
                OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<Guid>(type: "uuid", nullable: false),
                RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_OutboxState", x => x.OutboxId));

        migrationBuilder.CreateTable(
            name: "payment_customers",
            columns: table => new
            {
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                IdentityProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IdentitySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StripeCustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_payment_customers", x => x.CustomerId));

        migrationBuilder.CreateTable(
            name: "stripe_webhook_inbox",
            columns: table => new
            {
                EventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ObjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ProcessingLeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                ProcessingLeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_stripe_webhook_inbox", x => x.EventId));

        migrationBuilder.CreateTable(
            name: "OutboxMessage",
            columns: table => new
            {
                SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Headers = table.Column<string>(type: "text", nullable: true),
                Properties = table.Column<string>(type: "text", nullable: true),
                InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                MessageType = table.Column<string>(type: "text", nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                table.ForeignKey("FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId", x => new { x.InboxMessageId, x.InboxConsumerId }, "InboxState", new[] { "MessageId", "ConsumerId" });
                table.ForeignKey("FK_OutboxMessage_OutboxState_OutboxId", x => x.OutboxId, "OutboxState", "OutboxId");
            });

        migrationBuilder.CreateTable(
            name: "payment_methods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderPaymentMethodId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Brand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                ExpMonth = table.Column<int>(type: "integer", nullable: true),
                ExpYear = table.Column<int>(type: "integer", nullable: true),
                WalletType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_methods", x => x.Id);
                table.ForeignKey("FK_payment_methods_payment_customers_CustomerId", x => x.CustomerId, "payment_customers", "CustomerId", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_InboxState_Delivered", "InboxState", "Delivered");
        migrationBuilder.CreateIndex("IX_OutboxMessage_EnqueueTime", "OutboxMessage", "EnqueueTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_ExpirationTime", "OutboxMessage", "ExpirationTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber", "OutboxMessage", new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_OutboxMessage_OutboxId_SequenceNumber", "OutboxMessage", new[] { "OutboxId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_OutboxState_Created", "OutboxState", "Created");
        migrationBuilder.CreateIndex("IX_payment_methods_CustomerId", "payment_methods", "CustomerId");
        migrationBuilder.CreateIndex("UX_payment_methods_provider_id", "payment_methods", "ProviderPaymentMethodId", unique: true);
        migrationBuilder.CreateIndex("UX_payment_methods_default", "payment_methods", new[] { "CustomerId", "IsDefault" }, unique: true, filter: "\"IsDefault\"");
        migrationBuilder.CreateIndex("UX_payment_customers_identity", "payment_customers", new[] { "IdentityProvider", "IdentitySubject" }, unique: true);
        migrationBuilder.CreateIndex("UX_payment_customers_stripe_customer_id", "payment_customers", "StripeCustomerId", unique: true, filter: "\"StripeCustomerId\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_stripe_webhook_inbox_ProcessedAtUtc_NextAttemptAtUtc_ReceivedAtUtc", "stripe_webhook_inbox", new[] { "ProcessedAtUtc", "NextAttemptAtUtc", "ReceivedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("OutboxMessage");
        migrationBuilder.DropTable("payment_methods");
        migrationBuilder.DropTable("stripe_webhook_inbox");
        migrationBuilder.DropTable("InboxState");
        migrationBuilder.DropTable("OutboxState");
        migrationBuilder.DropTable("payment_customers");
    }
}
