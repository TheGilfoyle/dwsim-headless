namespace DwsimService.Models.Responses;

public record OutletStreamResult(
    double Temperature,
    double Pressure,
    double TotalFlow,
    string FlowBasis,
    Dictionary<string, double> Composition,
    double VaporFraction,
    double? Enthalpy,
    double? Entropy
);

public record ReactorProfilesResult(
    List<double> Position,
    List<double> Temperature,
    Dictionary<string, List<double>> Compositions
);

public record ReactorTransientProfilesResult(
    List<double> Time,
    List<double> Temperature,
    List<double> Pressure,
    Dictionary<string, List<double>> Compositions
);

public record ReactorSimulationResponse(
    string Status,
    OutletStreamResult? OutletStream,
    Dictionary<string, double>? Conversions,
    double? HeatDuty,
    double? ResidenceTime,
    ReactorProfilesResult? Profiles,
    ReactorTransientProfilesResult? TransientProfiles,
    List<string> Errors,
    List<string> Warnings
);
