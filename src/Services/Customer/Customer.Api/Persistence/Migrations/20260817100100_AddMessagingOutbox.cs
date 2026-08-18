using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Customer.Api.Persistence.Migrations;

[DbContext(typeof(CustomerDbContext))]
[Migration("20260817100100_AddMessagingOutbox")]
public partial class AddMessagingOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("InboxState", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
        }, constraints: table =>
        {
            table.PrimaryKey("PK_InboxState", x => x.Id);
            table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
        });

        migrationBuilder.CreateTable("OutboxState", table => new
        {
            OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
            LockId = table.Column<Guid>(type: "uuid", nullable: false),
            RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
            Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_OutboxState", x => x.OutboxId));

        migrationBuilder.CreateTable("OutboxMessage", table => new
        {
            SequenceNumber = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
        }, constraints: table =>
        {
            table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
            table.ForeignKey("FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId", x => new { x.InboxMessageId, x.InboxConsumerId }, "InboxState", new[] { "MessageId", "ConsumerId" });
            table.ForeignKey("FK_OutboxMessage_OutboxState_OutboxId", x => x.OutboxId, "OutboxState", "OutboxId");
        });

        migrationBuilder.CreateIndex("IX_InboxState_Delivered", "InboxState", "Delivered");
        migrationBuilder.CreateIndex("IX_OutboxState_Created", "OutboxState", "Created");
        migrationBuilder.CreateIndex("IX_OutboxMessage_EnqueueTime", "OutboxMessage", "EnqueueTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_ExpirationTime", "OutboxMessage", "ExpirationTime");
        migrationBuilder.CreateIndex("IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber", "OutboxMessage", new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_OutboxMessage_OutboxId_SequenceNumber", "OutboxMessage", new[] { "OutboxId", "SequenceNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("OutboxMessage");
        migrationBuilder.DropTable("InboxState");
        migrationBuilder.DropTable("OutboxState");
    }
}
