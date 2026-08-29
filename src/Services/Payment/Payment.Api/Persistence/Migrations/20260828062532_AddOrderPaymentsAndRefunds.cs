using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentsAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProviderSetupIntentId",
                table: "payment_webhook_events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPaymentIntentId",
                table: "payment_webhook_events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRefundId",
                table: "payment_webhook_events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WalletType",
                table: "payment_methods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Last4",
                table: "payment_methods",
                type: "character(4)",
                fixedLength: true,
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Brand",
                table: "payment_methods",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.CreateTable(
                name: "order_payment_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderPaymentIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderRefundId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RejectionCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_payment_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_payment_attempts_payment_customers_PaymentCustomerId",
                        column: x => x.PaymentCustomerId,
                        principalTable: "payment_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_payment_attempts_payment_methods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "payment_methods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ProviderPaymentIntentId",
                table: "payment_webhook_events",
                column: "ProviderPaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_webhook_events_ProviderRefundId",
                table: "payment_webhook_events",
                column: "ProviderRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payment_attempts_PaymentCustomerId",
                table: "order_payment_attempts",
                column: "PaymentCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_order_payment_attempts_PaymentMethodId",
                table: "order_payment_attempts",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "ux_order_payment_attempts_order_id",
                table: "order_payment_attempts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_order_payment_attempts_provider_intent",
                table: "order_payment_attempts",
                column: "ProviderPaymentIntentId",
                unique: true,
                filter: "\"ProviderPaymentIntentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_order_payment_attempts_provider_refund",
                table: "order_payment_attempts",
                column: "ProviderRefundId",
                unique: true,
                filter: "\"ProviderRefundId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_payment_attempts");

            migrationBuilder.DropIndex(
                name: "IX_payment_webhook_events_ProviderPaymentIntentId",
                table: "payment_webhook_events");

            migrationBuilder.DropIndex(
                name: "IX_payment_webhook_events_ProviderRefundId",
                table: "payment_webhook_events");

            migrationBuilder.DropColumn(
                name: "ProviderPaymentIntentId",
                table: "payment_webhook_events");

            migrationBuilder.DropColumn(
                name: "ProviderRefundId",
                table: "payment_webhook_events");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderSetupIntentId",
                table: "payment_webhook_events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WalletType",
                table: "payment_methods",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Last4",
                table: "payment_methods",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character(4)",
                oldFixedLength: true,
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "Brand",
                table: "payment_methods",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
