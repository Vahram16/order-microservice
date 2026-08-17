using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payment.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayment : Migration
    {
        /// <inheritdoc />
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
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "payment_customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdentitySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderSetupIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_webhook_events", x => x.Id);
                });

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
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateTable(
                name: "payment_method_setups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderSetupIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_method_setups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_method_setups_payment_customers_PaymentCustomerId",
                        column: x => x.PaymentCustomerId,
                        principalTable: "payment_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderPaymentMethodId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Brand = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    ExpMonth = table.Column<int>(type: "integer", nullable: false),
                    ExpYear = table.Column<int>(type: "integer", nullable: false),
                    WalletType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_methods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_methods_payment_customers_PaymentCustomerId",
                        column: x => x.PaymentCustomerId,
                        principalTable: "payment_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxState_Delivered",
                table: "InboxState",
                column: "Delivered");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "ux_payment_customers_customer_id",
                table: "payment_customers",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_customers_identity",
                table: "payment_customers",
                columns: new[] { "IdentityProvider", "IdentitySubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payment_customers_provider_customer",
                table: "payment_customers",
                column: "ProviderCustomerId",
                unique: true,
                filter: "\"ProviderCustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_method_setups_PaymentCustomerId",
                table: "payment_method_setups",
                column: "PaymentCustomerId");

            migrationBuilder.CreateIndex(
                name: "ux_payment_method_setups_provider_intent",
                table: "payment_method_setups",
                column: "ProviderSetupIntentId",
                unique: true,
                filter: "\"ProviderSetupIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_methods_PaymentCustomerId",
                table: "payment_methods",
                column: "PaymentCustomerId");

            migrationBuilder.CreateIndex(
                name: "ux_payment_methods_default",
                table: "payment_methods",
                columns: new[] { "PaymentCustomerId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "ux_payment_methods_provider_id",
                table: "payment_methods",
                column: "ProviderPaymentMethodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ProcessedAt",
                table: "payment_webhook_events",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ProviderSetupIntentId",
                table: "payment_webhook_events",
                column: "ProviderSetupIntentId");

            migrationBuilder.CreateIndex(
                name: "ux_payment_webhook_events_provider_event",
                table: "payment_webhook_events",
                column: "ProviderEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "payment_method_setups");

            migrationBuilder.DropTable(
                name: "payment_methods");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropTable(
                name: "payment_customers");
        }
    }
}
