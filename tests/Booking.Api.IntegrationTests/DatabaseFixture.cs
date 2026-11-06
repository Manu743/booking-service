using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Booking.Api.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

    private readonly string _repositoryRoot = FindRepositoryRoot();

    private BookingApiFactory? _factory;

    public string ConnectionString { get; private set; } = string.Empty;

    public BookingApiFactory Factory => _factory
        ?? throw new InvalidOperationException(
            "The database fixture has not been initialized.");

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Docker is required to run the integration tests. " +
                "Start Docker Desktop and re-run 'dotnet test BookingService.slnx'.",
                exception);
        }

        var masterConnectionString = _container.GetConnectionString();

        await SqlScriptRunner.ExecuteAsync(
            masterConnectionString,
            Path.Combine(_repositoryRoot, "db", "001_schema.sql"));

        ConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = "BookingServiceDb"
        }.ConnectionString;

        await SqlScriptRunner.ExecuteAsync(
            ConnectionString,
            Path.Combine(_repositoryRoot, "db", "003_create_booking_procedure.sql"));

        await ResetAsync();

        foreach (var script in new[]
                 {
                     "010_utilisation.sql",
                     "011_top_customers.sql",
                     "012_free_windows.sql",
                     "013_data_quality.sql"
                 })
        {
            await SqlScriptRunner.ExecuteAsync(
                ConnectionString,
                Path.Combine(_repositoryRoot, "db", script));
        }

        _factory = new BookingApiFactory(ConnectionString);
    }

    public Task ResetAsync() => SqlScriptRunner.ExecuteAsync(
        ConnectionString,
        Path.Combine(_repositoryRoot, "db", "002_seed_golden.sql"));

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookingService.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
