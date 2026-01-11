using Microsoft.AspNetCore.Mvc.Testing;

namespace AtlasX.Api.Tests;

public class MetricsTests
{
    [Fact]
    public async Task Metrics_endpoint_is_available()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/metrics");
        response.EnsureSuccessStatusCode();
    }
}
