using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DwsimService.Tests;

public class ReactorEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ReactorEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReactorPfr_SteadyState_ReturnsSpatialProfiles()
    {
        var response = await _client.PostAsJsonAsync("reactor/simulate", CreatePfrRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("success", root.GetProperty("status").GetString()?.ToLowerInvariant());
        Assert.True(root.TryGetProperty("profiles", out var profiles));
        Assert.Equal(JsonValueKind.Object, profiles.ValueKind);
        Assert.True(profiles.GetProperty("position").GetArrayLength() > 0);
        Assert.True(profiles.GetProperty("temperature").GetArrayLength() > 0);

        if (root.TryGetProperty("transientProfiles", out var transientProfiles))
            Assert.Equal(JsonValueKind.Null, transientProfiles.ValueKind);
    }

    [Fact]
    public async Task ReactorCstr_Dynamic_ReturnsTransientProfiles()
    {
        var response = await _client.PostAsJsonAsync("reactor/simulate", CreateDynamicCstrRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("success", root.GetProperty("status").GetString()?.ToLowerInvariant());
        Assert.True(root.TryGetProperty("transientProfiles", out var transientProfiles));
        Assert.Equal(JsonValueKind.Object, transientProfiles.ValueKind);

        var time = transientProfiles.GetProperty("time");
        var temperature = transientProfiles.GetProperty("temperature");
        var pressure = transientProfiles.GetProperty("pressure");
        var compositions = transientProfiles.GetProperty("compositions");

        Assert.Equal(4, time.GetArrayLength());
        Assert.Equal(time.GetArrayLength(), temperature.GetArrayLength());
        Assert.Equal(time.GetArrayLength(), pressure.GetArrayLength());
        Assert.True(compositions.GetProperty("Ethanol").GetArrayLength() == time.GetArrayLength());
        Assert.True(compositions.GetProperty("Ethyl Acetate").GetArrayLength() == time.GetArrayLength());
    }

    private static object CreatePfrRequest() => new
    {
        compounds = new[] { "Ethanol", "Water", "Acetic Acid", "Ethyl Acetate" },
        propertyPackage = "NRTL",
        reactorType = "PFR",
        inletStreams = new[]
        {
            new
            {
                temperature = 350.0,
                pressure = 101325.0,
                totalFlow = 100.0,
                flowBasis = "molar",
                composition = new Dictionary<string, double>
                {
                    ["Ethanol"] = 0.5,
                    ["Acetic Acid"] = 0.5,
                    ["Water"] = 0.0,
                    ["Ethyl Acetate"] = 0.0
                }
            }
        },
        reactions = new[]
        {
            CreateKineticReaction()
        },
        reactorLength = 1.0,
        reactorDiameter = 0.1,
        thermalMode = "adiabatic",
        numberOfSegments = 8,
        timeoutSeconds = 120
    };

    private static object CreateDynamicCstrRequest() => new
    {
        compounds = new[] { "Ethanol", "Water", "Acetic Acid", "Ethyl Acetate" },
        propertyPackage = "NRTL",
        reactorType = "CSTR",
        simulationMode = "dynamic",
        inletStreams = new[]
        {
            new
            {
                temperature = 350.0,
                pressure = 101325.0,
                totalFlow = 100.0,
                flowBasis = "molar",
                composition = new Dictionary<string, double>
                {
                    ["Ethanol"] = 0.5,
                    ["Acetic Acid"] = 0.5,
                    ["Water"] = 0.0,
                    ["Ethyl Acetate"] = 0.0
                }
            }
        },
        reactions = new[]
        {
            CreateKineticReaction()
        },
        transient = new
        {
            finalTime = 30.0,
            numberOfPoints = 4
        },
        reactorVolume = 1.0,
        thermalMode = "adiabatic",
        timeoutSeconds = 120
    };

    private static object CreateKineticReaction() => new
    {
        name = "Esterification",
        type = "Kinetic",
        compounds = new Dictionary<string, double>
        {
            ["Ethanol"] = -1.0,
            ["Acetic Acid"] = -1.0,
            ["Ethyl Acetate"] = 1.0,
            ["Water"] = 1.0
        },
        baseCompound = "Ethanol",
        phase = "Liquid",
        basis = "MolarConc",
        aForward = 1.0e6,
        eForward = 50000.0,
        aReverse = 0.0,
        eReverse = 0.0,
        directOrders = new Dictionary<string, double>
        {
            ["Ethanol"] = 1.0,
            ["Acetic Acid"] = 1.0,
            ["Ethyl Acetate"] = 0.0,
            ["Water"] = 0.0
        }
    };
}
