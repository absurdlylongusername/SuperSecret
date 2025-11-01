using Microsoft.Extensions.Configuration;

namespace SuperSecretTests.TestInfrastructure;
static class TestConfiguration
{
    public static IConfigurationRoot Configuration { get; } = new ConfigurationBuilder()
            .SetBasePath(TestContext.CurrentContext.TestDirectory)
            .AddJsonFile("testsettings.json", optional: true)
            .AddJsonFile("testsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

    public static IntegrationTestOptions Options { get; } = Configuration.GetRequiredSection(nameof(IntegrationTestOptions)).Get<IntegrationTestOptions>()
            ?? throw new Exception("Failed to bind IntegrationTestOptions");
}
