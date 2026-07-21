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
    }
}
