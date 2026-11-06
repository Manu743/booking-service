using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Booking.Api.IntegrationTests;

public sealed class SwaggerTests
{
    // Deliberately unreachable synthetic database; these tests never need SQL Server.
    private const string ConnectionString =
        "Server=127.0.0.1,1;Database=SyntheticSwagger;User Id=synthetic;Password=NotARealCredential!42;Connect Timeout=1;Encrypt=False";

    [Fact]
    public async Task Development_ServesSwaggerAndExistingOpenApi()
    {
        await using var factory = new BookingApiFactory(ConnectionString, "Development");
        using var client = CreateClient(factory, "https://localhost");
        using var redirect = await client.GetAsync("/swagger");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("swagger/index.html", redirect.Headers.Location?.OriginalString);

        using var page = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("swagger-ui", await page.Content.ReadAsStringAsync());

        using var initializer = await client.GetAsync("/swagger/index.js");
        Assert.Equal(HttpStatusCode.OK, initializer.StatusCode);
        Assert.Contains("/openapi/v1.json", await initializer.Content.ReadAsStringAsync());

        using var document = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var json = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/rooms", out _));
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task NonDevelopment_DoesNotExposeDocumentation(string environment)
    {
        await using var factory = new BookingApiFactory(ConnectionString, environment);
        using var client = CreateClient(factory, "https://localhost");
        foreach (var path in new[] { "/swagger", "/swagger/index.html", "/swagger/index.js", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task ExplicitConfiguration_PreservesUnhealthyDatabaseResponse(string environment)
    {
        await using var factory = new BookingApiFactory(ConnectionString, environment);
        using var client = CreateClient(factory, "https://localhost");
        using var response = await client.GetAsync("/health/database");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Database connection failed", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(503, json.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("NotARealCredential", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_PreservesHttpsRedirection()
    {
        await using var factory = new BookingApiFactory(ConnectionString, "Production");
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("https_port", "443"));
        using var client = CreateClient(configured, "http://localhost");
        using var response = await client.GetAsync("/health/database");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/health/database", response.Headers.Location?.AbsoluteUri);
    }

    [Fact]
    public async Task Testing_PreservesHttpWithoutRedirection()
    {
        await using var factory = new BookingApiFactory(ConnectionString);
        using var client = CreateClient(factory, "http://localhost");
        using var response = await client.GetAsync("/health/database");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string baseAddress) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false
        });
}
