namespace DwsimService.Models.Requests;

public record InletStreamRequest(
    double Temperature,
    double Pressure,
    double TotalFlow,
    string FlowBasis = "molar",
    Dictionary<string, double>? Composition = null
);

public record ReactionRequest(
    string Name,
    string Type,
    Dictionary<string, double> Compounds,
    string BaseCompound,
    string Phase = "Liquid",
    string Basis = "MolarConc",

    // Kinetic parameters
    double AForward = 0.0,
    double EForward = 0.0,
    double AReverse = 0.0,
    double EReverse = 0.0,
    Dictionary<string, double>? DirectOrders = null,
    Dictionary<string, double>? ReverseOrders = null,

    // Conversion parameters
    string? ConversionExpression = null,

    // Equilibrium parameters
    string? KeqExpression = null,
    double ApproachTemperature = 0.0
);

public record ReactorTransientSettingsRequest(
    double? FinalTime = null,
    double? TimeStep = null,
    int? NumberOfPoints = null,
    List<double>? TimeGrid = null,
    bool InitializeFromInlet = true,
    bool ResetContents = true
);

public record ReactorSimulationRequest(
    List<string> Compounds,
    string PropertyPackage,
    string ReactorType,
    List<InletStreamRequest> InletStreams,
    List<ReactionRequest> Reactions,

    // Simulation mode
    string SimulationMode = "steady_state",
    ReactorTransientSettingsRequest? Transient = null,

    // Reactor geometry
    double? ReactorVolume = null,
    double? ReactorLength = null,
    double? ReactorDiameter = null,
    int NumberOfTubes = 1,

    // Thermal mode
    string ThermalMode = "isothermal",
    double? OutletTemperature = null,
    double? HeatDuty = null,

    // Pressure
    double PressureDrop = 0.0,

    // Solver
    double ConvergenceTolerance = 1e-6,
    int MaxIterations = 100,
    int NumberOfSegments = 10,
    int TimeoutSeconds = 120
);
