using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microservices.Messaging.Tests;

public sealed class RabbitMqMessagingExtensionsTests
{
    [Fact]
    public void RegistrationUsesServicePrefixedKebabCaseEndpointNames()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqWithPostgresOutbox<TestDbContext>(
            ConnectionStringConfiguration("amqps://guest:secret@rabbitmq.example:5671/"),
            "service-template");

        using var provider = services.BuildServiceProvider();
        var formatter = provider.GetRequiredService<IEndpointNameFormatter>();

        Assert.IsType<KebabCaseEndpointNameFormatter>(formatter);
        Assert.Equal(
            "service-template-submit-order",
            formatter.Consumer<SubmitOrderConsumer>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ServiceTemplate")]
    [InlineData("service--template")]
    [InlineData("service-template-")]
    public void RegistrationRejectsInvalidEndpointPrefixes(string prefix)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(
                ConnectionStringConfiguration("amqps://guest:secret@rabbitmq.example:5671/"),
                prefix));
    }

    [Fact]
    public void RegistrationAcceptsValidFallbackConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Messaging:Host"] = "rabbitmq",
            ["Messaging:VirtualHost"] = "/",
            ["Messaging:Username"] = "guest",
            ["Messaging:Password"] = "guest",
            ["Messaging:Port"] = "5672",
            ["Messaging:UseTls"] = "false"
        });

        services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IEndpointNameFormatter));
    }

    [Fact]
    public void RegistrationRejectsIncompleteFallbackConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Messaging:Host"] = "rabbitmq",
            ["Messaging:Username"] = "guest",
            ["Messaging:UseTls"] = "false"
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));

        Assert.Contains(exception.Failures, failure => failure.Contains("Password", StringComparison.Ordinal));
        Assert.Equal(typeof(RabbitMqMessagingOptions), exception.OptionsType);
        Assert.Equal(RabbitMqMessagingOptions.SectionName, exception.OptionsName);
    }

    [Theory]
    [InlineData("http://rabbitmq.example/")]
    [InlineData("amqp://rabbitmq.example:5672/")]
    [InlineData("not-a-uri")]
    public void RegistrationRejectsInvalidOrInsecureProductionConnectionStrings(
        string connectionString)
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(
                ConnectionStringConfiguration(connectionString),
                "service-template"));
    }

    [Fact]
    public void RegistrationAllowsExplicitlyInsecureLocalConnectionString()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
            ["Messaging:UseTls"] = "false"
        });

        services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IEndpointNameFormatter));
    }

    [Fact]
    public void RegistrationRejectsNonRabbitMqSchemeEvenWhenTlsIsDisabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "http://guest:secret@localhost/",
            ["Messaging:UseTls"] = "false"
        });

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));
    }

    [Fact]
    public void RegistrationRejectsInsecureUriSchemeOnTlsPort()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqp://guest:secret@localhost:5671/",
            ["Messaging:UseTls"] = "false"
        });

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));
    }

    [Fact]
    public void ValidationErrorsDoNotExposeConnectionStringCredentials()
    {
        const string secret = "do-not-log-this-secret";
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = $"http://guest:{secret}@localhost/",
            ["Messaging:UseTls"] = "false"
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsInvalidOutboxDurations()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = "amqps://rabbitmq.example:5671/",
            ["Messaging:OutboxQueryDelay"] = "00:01:00",
            ["Messaging:DuplicateDetectionWindow"] = "00:00:30"
        });

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));
    }

    [Fact]
    public void RegistrationRejectsPortZero()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Messaging:Host"] = "rabbitmq",
            ["Messaging:VirtualHost"] = "/",
            ["Messaging:Username"] = "guest",
            ["Messaging:Password"] = "guest",
            ["Messaging:Port"] = "0",
            ["Messaging:UseTls"] = "false"
        });

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));
    }

    [Fact]
    public void RegistrationRejectsTlsPortWhenTlsIsDisabled()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Messaging:Host"] = "rabbitmq",
            ["Messaging:VirtualHost"] = "/",
            ["Messaging:Username"] = "guest",
            ["Messaging:Password"] = "guest",
            ["Messaging:Port"] = "5671",
            ["Messaging:UseTls"] = "false"
        });

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(configuration, "service-template"));
    }

    [Fact]
    public void ConfigurationContractNamesRemainStable()
    {
        Assert.Equal("Messaging", RabbitMqMessagingOptions.SectionName);
        Assert.Equal("rabbitmq", RabbitMqMessagingOptions.ConnectionStringName);
    }

    [Fact]
    public void RegistrationRejectsPortZeroInConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddRabbitMqWithPostgresOutbox<TestDbContext>(
                ConnectionStringConfiguration("amqps://guest:secret@rabbitmq.example:0/"),
                "service-template"));
    }

    [Theory]
    [InlineData(null, "rabbitmq.example")]
    [InlineData("broker.internal", "broker.internal")]
    public void TlsConfigurationUsesSystemPolicyAndStrictCertificateValidation(
        string? configuredServerName,
        string expectedServerName)
    {
        var ssl = DispatchProxy.Create<IRabbitMqSslConfigurator, RecordingSslConfigurator>();
        var recorder = (RecordingSslConfigurator)(object)ssl;

        RabbitMqMessagingExtensions.ConfigureTls(
            ssl,
            new RabbitMqMessagingOptions { TlsServerName = configuredServerName },
            "rabbitmq.example");

        Assert.True(recorder.Protocol.HasValue);
        Assert.Equal(SslProtocols.None, recorder.Protocol.GetValueOrDefault());
        Assert.Equal(expectedServerName, recorder.ServerName);
        Assert.True(recorder.EnforcedPolicyErrors.HasValue);
        Assert.Equal(
            SslPolicyErrors.RemoteCertificateChainErrors |
            SslPolicyErrors.RemoteCertificateNameMismatch |
            SslPolicyErrors.RemoteCertificateNotAvailable,
            recorder.EnforcedPolicyErrors.GetValueOrDefault());
    }

    private static IConfiguration ConnectionStringConfiguration(string value) =>
        Configuration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:rabbitmq"] = value
        });

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class TestDbContext : DbContext;

    private sealed record SubmitOrder;

    private sealed class SubmitOrderConsumer : IConsumer<SubmitOrder>
    {
        public Task Consume(ConsumeContext<SubmitOrder> context) => Task.CompletedTask;
    }

    public class RecordingSslConfigurator : DispatchProxy
    {
        public SslProtocols? Protocol { get; private set; }

        public string? ServerName { get; private set; }

        public SslPolicyErrors? EnforcedPolicyErrors { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            ArgumentNullException.ThrowIfNull(args);

            switch (targetMethod.Name)
            {
                case "set_Protocol":
                    Protocol = (SslProtocols)args[0]!;
                    return null;
                case "set_ServerName":
                    ServerName = (string)args[0]!;
                    return null;
                case nameof(IRabbitMqSslConfigurator.EnforcePolicyErrors):
                    EnforcedPolicyErrors = (SslPolicyErrors)args[0]!;
                    return null;
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }
    }
}
