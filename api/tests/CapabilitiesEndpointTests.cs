using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DwsimService.Tests;

public class CapabilitiesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CapabilitiesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Capabilities_ReturnsReactorSupportMetadata()
    {
        var response = await _client.GetAsync("/capabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var reactorSupport = root.GetProperty("reactorSupport");
        Assert.True(reactorSupport.GetProperty("reactorTypes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Contains("CSTR"));
        Assert.True(reactorSupport.GetProperty("reactorTypes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Contains("PFR"));

        var dynamicMode = reactorSupport.GetProperty("simulationModes").GetProperty("dynamic");
        Assert.True(dynamicMode.GetProperty("supportedThermalModes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Contains("adiabatic"));
        Assert.False(dynamicMode.GetProperty("supportedThermalModes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Contains("isothermal"));
    }
}
