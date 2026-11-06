using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

public sealed class ContainerConfigurationTests
{
    [Fact]
    public void Dockerfile_UsesNet10NonRootAndDatabaseHealthProbe()
    {
        var dockerfile = Read("Dockerfile");
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", dockerfile);
        Assert.Contains("mcr.microsoft.com/dotnet/aspnet:10.0", dockerfile);
        Assert.Contains("USER $APP_UID", dockerfile);
        Assert.Contains("ASPNETCORE_HTTP_PORTS=8080", dockerfile);
        Assert.Contains("EXPOSE 8080", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("/health/database", dockerfile);
    }

    [Fact]
    public void Compose_UsesAssessmentDefaultAndOrdersInitializationWithoutPublishingSql()
    {
        var compose = Read("docker-compose.yml");
        Assert.Contains("mcr.microsoft.com/mssql/server:2022-latest", compose);
        Assert.Equal(3, compose.Split("${MSSQL_SA_PASSWORD:-LocalAssessmentOnly!2026}").Length - 1);
        Assert.DoesNotContain("${MSSQL_SA_PASSWORD:?", compose);
        Assert.Contains("ConnectionStrings__BookingDatabase:", compose);
        Assert.DoesNotContain("ConnectionStrings__Default", compose);
        Assert.Contains("condition: service_healthy", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: Development", compose);
        Assert.Contains("127.0.0.1:8080:8080", compose);
        Assert.DoesNotContain("1433:", compose);
        Assert.Contains("sql-data:/var/opt/mssql", compose);
        Assert.Contains("./db:/db:ro", compose);
    }

    [Fact]
    public void Context_IsAllowListedAndExcludesSecretsPrivateDocsAndBuildOutput()
    {
        var ignore = Read(".dockerignore");
        Assert.StartsWith("# Only API build inputs", ignore);
        foreach (var rule in new[] { "**/.env", "**/.env.*", "**/bin", "**/obj", "**/*.pfx", "**/*.key", "docs", ".git" })
            Assert.Contains(rule, ignore);
        Assert.Contains("!src/Booking.Api/**", ignore);
        Assert.Contains("!src/Booking.Domain/**", ignore);
    }

    [Fact]
    public void Initialization_FailsFastAndNeverSeedsAnExistingDatabase()
    {
        var init = Read("db/init.sh");
        Assert.Contains("set -euo pipefail", init);
        Assert.Contains("-b -V 16", init);
        Assert.DoesNotContain(" -P ", init);
        Assert.Contains("SQLCMDPASSWORD", init);
        Assert.Contains("Skipping initialized assessment database", init);
        Assert.Contains("Refusing to initialize an existing unmarked database", init);
        Assert.Contains("000_create_database.sql", init);
        Assert.Contains("001_schema.sql", init);
        Assert.Contains("002_seed_golden.sql", init);
        Assert.Contains("003_create_booking_procedure.sql", init);
        Assert.Contains("013_data_quality.sql", init);
        var guard = Read("db/000_create_database.sql");
        Assert.Contains("THROW", guard);
        Assert.DoesNotContain("DROP", guard);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Compose_ConfigDefaultsMissingOrEmptyPasswordWithoutCreatingContainers(string? password)
    {
        await CheckComposeConfig(password);
    }

    [Fact]
    public async Task Compose_ConfigPreservesSuppliedPasswordAndSemicolonAcrossConsumers()
    {
        await CheckComposeConfig($"Aa1!{Guid.NewGuid():N};{Guid.NewGuid():N}");
    }

    private static async Task CheckComposeConfig(string? password)
    {
        var root = Root();
        var scratch = Path.Combine(root, $".phase8-empty-env.{Guid.NewGuid():N}");
        var project = $"booking-config-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(scratch, "");
        try
        {
            var start = new ProcessStartInfo("docker")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "compose", "--env-file", scratch, "-p", project, "config", "--format", "json" })
                start.ArgumentList.Add(argument);
            start.Environment.Remove("MSSQL_SA_PASSWORD");
            if (password is not null) start.Environment["MSSQL_SA_PASSWORD"] = password;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await output;
            var stderr = await error; // Never print resolved configuration or credentials.
            if (string.IsNullOrEmpty(password))
            {
                var inspect = new ProcessStartInfo("docker")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (var arg in new[] { "ps", "-aq", "--filter", $"label=com.docker.compose.project={project}" })
                    inspect.ArgumentList.Add(arg);
                using var probe = Process.Start(inspect)!;
                var containers = probe.StandardOutput.ReadToEndAsync();
                var diagnostics = probe.StandardError.ReadToEndAsync();
                await probe.WaitForExitAsync(timeout.Token);
                Assert.True(probe.ExitCode == 0, "Container inspection must succeed.");
                Assert.Equal("", await containers);
                Assert.Equal("", await diagnostics);
            }
            Assert.True(process.ExitCode == 0, "Compose config must accept defaults and optional overrides.");
            Assert.Equal("", stderr);
            using var config = JsonDocument.Parse(stdout);
            var services = config.RootElement.GetProperty("services");
            var expected = string.IsNullOrEmpty(password) ? "LocalAssessmentOnly!2026" : password;
            foreach (var service in new[] { "mssql", "db-init" })
                Assert.True(services.GetProperty(service).GetProperty("environment").GetProperty("MSSQL_SA_PASSWORD").GetString() == expected,
                    "SQL and initializer must use the selected assessment credential.");
            Assert.True(services.GetProperty("api").GetProperty("environment").GetProperty("ConnectionStrings__BookingDatabase").GetString() ==
                $"Server=mssql;Database=BookingServiceDb;User Id=sa;Password=\"{expected}\";Encrypt=True;TrustServerCertificate=True;",
                "API must preserve the selected credential, including embedded semicolons.");
            var port = services.GetProperty("api").GetProperty("ports")[0];
            Assert.Equal("127.0.0.1", port.GetProperty("host_ip").GetString());
            Assert.False(services.GetProperty("mssql").TryGetProperty("ports", out _));
        }
        finally { File.Delete(scratch); }
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root(), relativePath));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BookingService.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
