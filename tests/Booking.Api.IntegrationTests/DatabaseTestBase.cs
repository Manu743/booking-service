using Xunit;

namespace Booking.Api.IntegrationTests;

public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected DatabaseTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected DatabaseFixture Fixture { get; }

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected HttpClient CreateClient() => Fixture.Factory.CreateClient();
}
