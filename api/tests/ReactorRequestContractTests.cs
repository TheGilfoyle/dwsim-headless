using System.Dynamic;
using System.Text.Json;
using DwsimService.Infrastructure;
using DwsimService.Models.Requests;
using DwsimService.Services;

namespace DwsimService.Tests;

public class ReactorRequestContractTests
{
    [Fact]
    public void JsonBinding_MapsHeadspace_ToRequestModel()
    {
        var json = """
        {
          "compounds": ["Water"],
          "propertyPackage": "Peng-Robinson (PR)",
          "reactorType": "CSTR",
          "inletStreams": [
            {
              "temperature": 350.0,
              "pressure": 101325.0,
              "totalFlow": 1.0,
              "flowBasis": "molar"
            }
          ],
          "reactions": [
            {
              "name": "VaporReaction",
              "type": "Kinetic",
              "compounds": { "Water": -1.0 },
              "baseCompound": "Water",
              "phase": "Vapor"
            }
          ],
          "reactorVolume": 0.001,
          "headspace": 0.001
        }
        """;

        var req = JsonSerializer.Deserialize<ReactorSimulationRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(req);
        Assert.Equal(0.001, req!.Headspace);
    }

    [Theory]
    [InlineData("Vapor")]
    [InlineData("vapor")]
    public void ValidateRequest_RequiresHeadspace_ForVaporPhaseCstr(string phase)
    {
        var req = CreateRequest(reactorType: "CSTR", headspace: null, phase: phase);

        var ex = Assert.Throws<ApiValidationException>(() =>
            ReactorSimulationService.ValidateRequest(req));

        Assert.Equal("reactor_headspace_required_for_vapor_cstr", ex.Code);
        Assert.Equal("Headspace is required for CSTR vapor-phase reactions.", ex.Message);
    }

    [Fact]
    public void ValidateRequest_RejectsHeadspace_OnPfr()
    {
        var req = CreateRequest(reactorType: "PFR", headspace: 0.001, phase: "Vapor");

        var ex = Assert.Throws<ApiValidationException>(() =>
            ReactorSimulationService.ValidateRequest(req));

        Assert.Equal("reactor_headspace_cstr_only", ex.Code);
        Assert.Equal("Headspace is supported only for CSTR reactors.", ex.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ValidateRequest_RejectsInvalidHeadspace(double headspace)
    {
        var req = CreateRequest(reactorType: "CSTR", headspace: headspace, phase: "Liquid");

        var ex = Assert.Throws<ApiValidationException>(() =>
            ReactorSimulationService.ValidateRequest(req));

        Assert.Equal("reactor_headspace_invalid", ex.Code);
    }

    [Fact]
    public void ValidateRequest_AllowsMissingHeadspace_ForLiquidPhaseCstr()
    {
        var req = CreateRequest(reactorType: "CSTR", headspace: null, phase: "Liquid");

        ReactorSimulationService.ValidateRequest(req);
    }

    [Fact]
    public void ApplyCstrGeometry_SetsVolumeAndHeadspace()
    {
        var req = CreateRequest(reactorType: "CSTR", headspace: 0.001, phase: "Vapor");
        var reactor = new FakeDynamicReactor();

        ReactorSimulationService.ApplyCstrGeometry(reactor, req);

        Assert.Equal(0.001, (double)reactor.Values["Volume"]!);
        Assert.Equal(0.001, (double)reactor.Values["Headspace"]!);
    }

    [Theory]
    [InlineData("isothermal", FakeOperationMode.Isothermic)]
    [InlineData("adiabatic", FakeOperationMode.Adiabatic)]
    [InlineData("outlet_temperature", FakeOperationMode.OutletTemperature)]
    [InlineData("defined_duty", FakeOperationMode.NonIsothermalNonAdiabatic)]
    public void SetReactorOperationMode_UsesDwsimEnum(
        string thermalMode,
        FakeOperationMode expected)
    {
        var reactor = new FakeEnumReactor();

        ReactorSimulationService.SetReactorOperationMode(reactor, thermalMode);

        Assert.Equal(expected, reactor.ReactorOperationMode);
    }

    private static ReactorSimulationRequest CreateRequest(
        string reactorType,
        double? headspace,
        string phase)
    {
        return new ReactorSimulationRequest(
            Compounds: new List<string> { "Water" },
            PropertyPackage: "Peng-Robinson (PR)",
            ReactorType: reactorType,
            InletStreams: new List<InletStreamRequest>
            {
                new(
                    Temperature: 350.0,
                    Pressure: 101325.0,
                    TotalFlow: 1.0,
                    FlowBasis: "molar")
            },
            Reactions: new List<ReactionRequest>
            {
                new(
                    Name: "R1",
                    Type: "Kinetic",
                    Compounds: new Dictionary<string, double> { { "Water", -1.0 } },
                    BaseCompound: "Water",
                    Phase: phase)
            },
            ReactorVolume: 0.001,
            Headspace: headspace
        );
    }

    private sealed class FakeDynamicReactor : DynamicObject
    {
        public Dictionary<string, object?> Values { get; } = new();

        public override bool TrySetMember(SetMemberBinder binder, object? value)
        {
            Values[binder.Name] = value;
            return true;
        }
    }

    public enum FakeOperationMode
    {
        Isothermic,
        Adiabatic,
        OutletTemperature,
        NonIsothermalNonAdiabatic
    }

    private sealed class FakeEnumReactor
    {
        public FakeOperationMode ReactorOperationMode { get; set; }
    }
}
