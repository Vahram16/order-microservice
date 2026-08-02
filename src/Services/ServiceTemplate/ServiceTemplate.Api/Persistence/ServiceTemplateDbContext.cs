using MassTransit.EntityFrameworkCoreIntegration;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ServiceTemplate.Api.Persistence;

public sealed class ServiceTemplateDbContext(DbContextOptions<ServiceTemplateDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddMassTransitOutboxEntities();

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(message => new { message.SentTime, message.OutboxId })
            .HasDatabaseName("IX_OutboxMessage_BusPending_SentTime")
            .HasFilter("\"OutboxId\" IS NOT NULL");

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(message => new
            {
                message.SentTime,
                message.InboxMessageId,
                message.InboxConsumerId
            })
            .HasDatabaseName("IX_OutboxMessage_ConsumerPending_SentTime")
            .HasFilter(
                "\"OutboxId\" IS NULL AND \"InboxMessageId\" IS NOT NULL AND \"InboxConsumerId\" IS NOT NULL");
    }
}
