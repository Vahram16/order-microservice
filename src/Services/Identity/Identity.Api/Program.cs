using Identity.Api.Infrastructure;
using Identity.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddIdentityService();
builder.Services.AddIdentityApplication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<IdentityExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityServiceDbContext>();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Identity.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.FormFieldName = "__RequestVerificationToken";
});

var app = builder.Build();

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseIdentitySecurityHeaders();
app.UseRouting();
app.UseWhen(
    context =>
        !context.Request.Path.StartsWithSegments("/connect/authorize") &&
        !context.Request.Path.StartsWithSegments("/account"),
    branch => branch.UseCors(IdentityServiceExtensions.BrowserCorsPolicy));
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDefaultEndpoints();
app.MapIdentityApplication();

await app.RunAsync();

public partial class Program;
