using Customer.Api.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Customer.Api.Persistence.Migrations;

[DbContext(typeof(CustomerDbContext))]
[Migration("20260729113000_InitialCustomer")]
public partial class InitialCustomer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IdentityProvider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                IdentitySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "customer_addresses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PostalCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                IsDefaultShipping = table.Column<bool>(type: "boolean", nullable: false),
                IsDefaultBilling = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_addresses", x => x.Id);
                table.ForeignKey(
                    name: "FK_customer_addresses_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_customers_IdentityProvider_IdentitySubject",
            table: "customers",
            columns: new[] { "IdentityProvider", "IdentitySubject" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_customer_addresses_CustomerId",
            table: "customer_addresses",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "UX_customer_addresses_default_shipping",
            table: "customer_addresses",
            columns: new[] { "CustomerId", "IsDefaultShipping" },
            unique: true,
            filter: "\"IsDefaultShipping\"");

        migrationBuilder.CreateIndex(
            name: "UX_customer_addresses_default_billing",
            table: "customer_addresses",
            columns: new[] { "CustomerId", "IsDefaultBilling" },
            unique: true,
            filter: "\"IsDefaultBilling\"");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_addresses");
        migrationBuilder.DropTable(name: "customers");
    }
}
