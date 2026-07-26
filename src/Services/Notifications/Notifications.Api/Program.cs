using Notifications.Api.Features.IdentityNotifications.Receive.V1;
using Notifications.Api.Infrastructure;
using Notifications.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation("Notifications API");
builder.AddNotificationService();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<NotificationDbContext>();
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseNotificationSecurityHeaders();
app.UseRouting();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapIdentityNotificationIngress();
app.MapApiDocumentation();

await app.RunAsync();

public partial class Program;
