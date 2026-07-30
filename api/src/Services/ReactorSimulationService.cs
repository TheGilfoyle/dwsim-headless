using DwsimService.Models.Requests;
using DwsimService.Models.Responses;
using DwsimService.Infrastructure;

namespace DwsimService.Services;

/// <summary>
/// Builds a DWSIM flowsheet with inlet stream → reactor → outlet stream,
/// solves it, and extracts results.
/// </summary>
public static class ReactorSimulationService
{
    private const double TimeComparisonTolerance = 1e-9;
    private const int MaxTransientOutputPoints = 500;
    private const double MinTransientTimeStepSeconds = 0.1;

    /// <summary>Run a reactor simulation and return results.</summary>
    public static ReactorSimulationResponse Simulate(
        DwsimEngine engine, ReactorSimulationRequest req)
    {
        ValidateRequest(req);

        var fs = engine.CreateConfiguredFlowsheet(req.Compounds, req.PropertyPackage);

        // 1. Create reactions and add to default reaction set
        AddReactions(fs, req);

        // 2. Create inlet stream
        var inlet = DwsimEngine.AddObjectToFlowsheet(fs, "MaterialStream", 100, 100, "INLET");
        ConfigureInletStream(inlet, req);

        // 3. Create outlet stream
        var outlet = DwsimEngine.AddObjectToFlowsheet(fs, "MaterialStream", 600, 100, "OUTLET");

        // 4. Create energy stream (DWSIM reactors always require one)
        var energyStream = DwsimEngine.AddObjectToFlowsheet(fs, "EnergyStream", 350, 200, "ENERGY");

        // 5. Create and configure reactor
        var reactor = CreateReactor(fs, energyStream, req);

        // 6. Connect objects
        //    Inlet stream → Reactor inlet (port 0)
        //    Reactor product outlet (port 0) → Outlet stream
        //    Energy stream → Reactor energy port (port 1)
        fs.ConnectObjects(inlet.GraphicObject, reactor.GraphicObject, 0, 0);
        fs.ConnectObjects(reactor.GraphicObject, outlet.GraphicObject, 0, 0);
        fs.ConnectObjects(energyStream.GraphicObject, reactor.GraphicObject, 0, 1);

        var warnings = new List<string>();
        List<string> errors;
        ReactorTransientProfilesResult? transientProfiles = null;

        // 7. Solve
        if (IsDynamicSimulation(req))
        {
            var transientResult = ExecuteTransientCstrSimulation(
                engine,
                (object)fs,
                (object)outlet,
                (object)reactor,
                req,
                warnings
            );
            errors = transientResult.Errors;
            transientProfiles = transientResult.Profiles;
        }
        else
        {
            errors = engine.Solve(fs, req.TimeoutSeconds);
        }

        if (errors.Count > 0)
        {
            return new ReactorSimulationResponse(
                Status: "error",
                OutletStream: null,
                Conversions: null,
                HeatDuty: null,
                ResidenceTime: null,
                Profiles: null,
                TransientProfiles: null,
                Errors: errors,
                Warnings: warnings
            );
        }

        // 8. Extract results
        return ExtractResults(outlet, reactor, req, warnings, transientProfiles);
    }

    internal static void ValidateRequest(ReactorSimulationRequest req)
    {
        if (req.Compounds.Count == 0)
            throw ValidationError(
                "At least one compound is required.",
                "reactor_compounds_required",
                "Provide at least one compound in the compounds collection.");

        if (req.InletStreams.Count == 0)
            throw ValidationError(
                "At least one inlet stream is required.",
                "reactor_inlet_stream_required",
                "Provide at least one inlet stream definition.");

        if (req.Reactions.Count == 0)
            throw ValidationError(
                "At least one reaction is required.",
                "reactor_reaction_required",
                "Provide at least one reaction definition.");

        var simulationMode = NormalizeSimulationMode(req.SimulationMode);
        var reactorType = req.ReactorType.ToUpperInvariant();
        if (reactorType != "CSTR" && reactorType != "PFR")
            throw ValidationError(
                $"Unsupported reactor type: {req.ReactorType}. Supported: CSTR, PFR.",
                "reactor_type_unsupported",
                "Use reactorType=\"CSTR\" or reactorType=\"PFR\".");

        if (reactorType == "CSTR" && (req.ReactorVolume == null || req.ReactorVolume <= 0))
            throw ValidationError(
                "ReactorVolume is required and must be positive for CSTR.",
                "reactor_volume_required_for_cstr",
                "Provide a positive reactorVolume value for CSTR simulations.");

        var hasVaporPhaseReactions = req.Reactions.Any(r =>
            IsVaporPhase(r.Phase));

        if (reactorType != "CSTR" && req.Headspace.HasValue)
            throw ValidationError(
                "Headspace is supported only for CSTR reactors.",
                "reactor_headspace_cstr_only",
                "Remove headspace or use reactorType=\"CSTR\".");

        if (reactorType == "CSTR" && req.Headspace.HasValue &&
            (!double.IsFinite(req.Headspace.Value) || req.Headspace <= 0))
            throw ValidationError(
                "Headspace must be a finite positive volume.",
                "reactor_headspace_invalid",
                "Provide headspace greater than zero in cubic metres.");

        if (reactorType == "CSTR" && hasVaporPhaseReactions && !req.Headspace.HasValue)
            throw ValidationError(
                "Headspace is required for CSTR vapor-phase reactions.",
                "reactor_headspace_required_for_vapor_cstr",
                "Provide a positive headspace volume in cubic metres.");

        if (reactorType == "PFR")
        {
            bool hasVolume = req.ReactorVolume > 0;
            bool hasLength = req.ReactorLength > 0;
            bool hasDiameter = req.ReactorDiameter > 0;
            if (!hasVolume && !(hasLength && hasDiameter))
                throw ValidationError(
                    "PFR requires either ReactorVolume or both ReactorLength and ReactorDiameter.",
                    "reactor_geometry_required_for_pfr",
                    "Provide reactorVolume for a compact PFR definition.",
                    "Or provide both reactorLength and reactorDiameter for a geometric PFR definition.");
        }

        var thermalMode = NormalizeThermalMode(req.ThermalMode);
        if (thermalMode == "outlet_temperature" && !req.OutletTemperature.HasValue)
            throw ValidationError(
                "OutletTemperature is required when ThermalMode is outlet_temperature.",
                "reactor_outlet_temperature_required",
                "Provide outletTemperature when thermalMode is outlet_temperature.");

        if (thermalMode == "defined_duty" && !req.HeatDuty.HasValue)
            throw ValidationError(
                "HeatDuty is required when ThermalMode is defined_duty.",
                "reactor_heat_duty_required",
                "Provide heatDuty when thermalMode is defined_duty.");

        if (simulationMode == "dynamic")
        {
            if (reactorType != "CSTR")
                throw ValidationError(
                    "Dynamic simulation is currently supported only for CSTR.",
                    "reactor_dynamic_requires_cstr",
                    "Use reactorType=\"CSTR\" for dynamic simulations.",
                    "Or switch simulationMode to steady_state for PFR.");

            if (thermalMode is "isothermal" or "outlet_temperature")
                throw ValidationError(
                    "Dynamic CSTR simulation does not support isothermal or outlet_temperature thermal modes.",
                    "reactor_dynamic_thermal_mode_unsupported",
                    "Use thermalMode=\"adiabatic\" or thermalMode=\"defined_duty\" for dynamic CSTR runs.",
                    "Or switch simulationMode to steady_state for isothermal and outlet_temperature modes.");

            ValidateTransientSettings(req.Transient);
        }
    }

    private static void AddReactions(dynamic fs, ReactorSimulationRequest req)
    {
        for (int i = 0; i < req.Reactions.Count; i++)
        {
            var rxn = req.Reactions[i];
            dynamic reaction;

            var stoich = new Dictionary<string, double>(rxn.Compounds);
            var reactionType = rxn.Type.ToLowerInvariant();

            switch (reactionType)
            {
                case "kinetic":
                    // DWSIM requires directorders/reverseorders to have an entry for
                    // every compound in stoichiometry — fill missing ones with 0.0
                    var directOrders = new Dictionary<string, double>();
                    var reverseOrders = new Dictionary<string, double>();
                    foreach (var key in stoich.Keys)
                    {
                        directOrders[key] = rxn.DirectOrders?.GetValueOrDefault(key, 0.0) ?? 0.0;
                        reverseOrders[key] = rxn.ReverseOrders?.GetValueOrDefault(key, 0.0) ?? 0.0;
                    }

                    reaction = fs.CreateKineticReaction(
                        rxn.Name,                                          // name
                        rxn.Name,                                          // description
                        stoich,                                            // compounds_and_stoichcoeffs
                        directOrders,                                      // directorders
                        reverseOrders,                                     // reverseorders
                        rxn.BaseCompound,                                  // basecompound
                        rxn.Phase.ToLowerInvariant(),                      // reactionphase
                        MapBasis(rxn.Basis),                               // basis
                        "mol/m3",                                          // amountunits (SI)
                        "mol/[m3.s]",                                      // rateunits (SI)
                        rxn.AForward,                                      // Aforward
                        rxn.EForward,                                      // Eforward (J/mol)
                        rxn.AReverse,                                      // Areverse
                        rxn.EReverse,                                      // Ereverse (J/mol)
                        "",                                                // Expr_forward (empty = use Arrhenius)
                        ""                                                 // Expr_reverse
                    );
                    break;

                case "conversion":
                    reaction = fs.CreateConversionReaction(
                        rxn.Name,
                        rxn.Name,
                        stoich,
                        rxn.BaseCompound,
                        rxn.Phase.ToLowerInvariant(),
                        rxn.ConversionExpression ?? "0.0"
                    );
                    break;

                case "equilibrium":
                    reaction = fs.CreateEquilibriumReaction(
                        rxn.Name,
                        rxn.Name,
                        stoich,
                        rxn.BaseCompound,
                        rxn.Phase.ToLowerInvariant(),
                        MapBasis(rxn.Basis),
                        "Pa",
                        rxn.ApproachTemperature,
                        rxn.KeqExpression ?? "0"
                    );
                    break;

                default:
                    throw ValidationError(
                        $"Unsupported reaction type: {rxn.Type}. Supported: Kinetic, Conversion, Equilibrium.",
                        "reactor_reaction_type_unsupported",
                        "Use reaction type Kinetic, Conversion, or Equilibrium.");
            }

            fs.AddReaction(reaction);
            string rxnId = reaction.ID.ToString();
            fs.AddReactionToSet(rxnId, "DefaultSet", true, i);
        }
    }

    private static string MapBasis(string basis)
    {
        return basis.ToLowerInvariant() switch
        {
            "molarconc" or "molar_conc" or "molar concentration" => "molar concentration",
            "massconc" or "mass_conc" or "mass concentration" => "mass concentration",
            "molarfrac" or "molar_frac" or "molar fraction" => "molar fraction",
            "massfrac" or "mass_frac" or "mass fraction" => "mass fraction",
            "partialpressure" or "partial_pressure" or "partial pressure" => "partial pressure",
            "activity" => "activity",
            "fugacity" => "fugacity",
            _ => "molar concentration"
        };
    }

    private static void ConfigureInletStream(dynamic stream, ReactorSimulationRequest req)
    {
        var inlet = req.InletStreams[0];

        stream.SetPropertyValue("PROP_MS_0", inlet.Temperature);    // Temperature (K)
        stream.SetPropertyValue("PROP_MS_1", inlet.Pressure);       // Pressure (Pa)

        // Set flow based on basis
        switch (inlet.FlowBasis.ToLowerInvariant())
        {
            case "molar":
                stream.SetPropertyValue("PROP_MS_3", inlet.TotalFlow);  // Molar flow (mol/s)
                break;
            case "mass":
                stream.SetPropertyValue("PROP_MS_2", inlet.TotalFlow);  // Mass flow (kg/s)
                break;
            case "volumetric":
                stream.SetPropertyValue("PROP_MS_4", inlet.TotalFlow);  // Vol flow (m³/s)
                break;
            default:
                stream.SetPropertyValue("PROP_MS_3", inlet.TotalFlow);
                break;
        }

        // Set composition using correct compound-level codes
        if (inlet.Composition != null)
        {
            var compounds = req.Compounds;
            var composition = new List<double>();
            foreach (var compound in compounds)
                composition.Add(inlet.Composition.GetValueOrDefault(compound, 0.0));

            DwsimEngine.SetStreamComposition(stream, compounds, composition);
        }
    }

    private static dynamic CreateReactor(dynamic fs, dynamic energyStream, ReactorSimulationRequest req)
    {
        var reactorType = req.ReactorType.ToUpperInvariant();
        string objectTypeName = reactorType == "CSTR" ? "RCT_CSTR" : "RCT_PFR";

        var reactor = DwsimEngine.AddObjectToFlowsheet(fs, objectTypeName, 350, 100, "REACTOR");

        // Set reaction set
        reactor.ReactionSetID = "DefaultSet";
        reactor.ReactionSetName = "Default Set";

        // Set pressure drop
        if (req.PressureDrop != 0)
            reactor.DeltaP = req.PressureDrop;

        // Set thermal mode
        var thermalMode = NormalizeThermalMode(req.ThermalMode);
        SetReactorOperationMode(reactor, thermalMode);
        switch (thermalMode)
        {
            case "outlet_temperature":
                reactor.OutletTemperature = req.OutletTemperature!.Value;
                break;
            case "defined_duty":
                energyStream.EnergyFlow = req.HeatDuty!.Value;
                break;
        }

        if (reactorType == "CSTR")
        {
            ApplyCstrGeometry(reactor, req);
        }
        else // PFR
        {
            if (req.ReactorLength > 0 && req.ReactorDiameter > 0)
            {
                reactor.Length = req.ReactorLength.Value;
                reactor.Diameter = req.ReactorDiameter.Value;
                // Volume = π/4 * D² * L * NumberOfTubes
                double vol = Math.PI / 4.0 * Math.Pow(req.ReactorDiameter.Value, 2)
                             * req.ReactorLength.Value * req.NumberOfTubes;
                reactor.Volume = vol;
            }
            else if (req.ReactorVolume > 0)
            {
                reactor.Volume = req.ReactorVolume.Value;
                // Default geometry if not specified
                reactor.Length = 1.0;
                reactor.Diameter = Math.Sqrt(4.0 * req.ReactorVolume.Value / (Math.PI * req.NumberOfTubes));
            }

            reactor.NumberOfTubes = req.NumberOfTubes;

            // dV = total volume / number of segments
            double totalVol = (double)reactor.Volume;
            reactor.dV = totalVol / Math.Max(req.NumberOfSegments, 1);
        }

        return reactor;
    }

    internal static void ApplyCstrGeometry(dynamic reactor, ReactorSimulationRequest req)
    {
        reactor.Volume = req.ReactorVolume!.Value;

        if (req.Headspace.HasValue)
            reactor.Headspace = req.Headspace.Value;
    }

    private static bool IsVaporPhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return false;

        return phase.Equals("Vapor", StringComparison.OrdinalIgnoreCase);
    }

    internal static void SetReactorOperationMode(dynamic reactor, string thermalMode)
    {
        string operationModeName = thermalMode switch
        {
            "isothermal" => "Isothermic",
            "adiabatic" => "Adiabatic",
            "outlet_temperature" => "OutletTemperature",
            "defined_duty" => "NonIsothermalNonAdiabatic",
            _ => throw new InvalidOperationException(
                $"Unexpected normalized thermal mode: {thermalMode}.")
        };

        var reactorType = ((object)reactor).GetType();
        var prop = reactorType.GetProperty("ReactorOperationMode");
        if (prop == null || !prop.PropertyType.IsEnum)
        {
            throw new InvalidOperationException(
                "DWSIM reactor object does not expose enum ReactorOperationMode property.");
        }

        object operationModeValue;
        try
        {
            operationModeValue = Enum.Parse(prop.PropertyType, operationModeName, ignoreCase: true);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidOperationException(
                $"DWSIM reactor mode '{operationModeName}' is not available in enum {prop.PropertyType.FullName}.", ex);
        }

        prop.SetValue((object)reactor, operationModeValue);
    }

    private static ReactorSimulationResponse ExtractResults(
        dynamic outlet, dynamic reactor, ReactorSimulationRequest req,
        List<string> warnings,
        ReactorTransientProfilesResult? transientProfiles = null)
    {
        // Outlet stream properties
        var outletComposition = DwsimEngine.GetPhaseComposition(outlet, req.Compounds, 102);

        var outletStream = new OutletStreamResult(
            Temperature: DwsimEngine.TryGetDouble(outlet, "PROP_MS_0"),
            Pressure: DwsimEngine.TryGetDouble(outlet, "PROP_MS_1"),
            TotalFlow: DwsimEngine.TryGetDouble(outlet, "PROP_MS_3"),  // Molar flow
            FlowBasis: "molar",
            Composition: outletComposition,
            VaporFraction: DwsimEngine.TryGetDouble(outlet, "PROP_MS_27"),
            Enthalpy: DwsimEngine.TryGetNullableDouble(outlet, "PROP_MS_9"),
            Entropy: DwsimEngine.TryGetNullableDouble(outlet, "PROP_MS_10")
        );

        // Conversions from reactor
        var conversions = new Dictionary<string, double>();
        try
        {
            var compConversions = reactor.ComponentConversions;
            if (compConversions != null)
            {
                foreach (var key in compConversions.Keys)
                {
                    string compName = key.ToString()!;
                    double conv = (double)compConversions[key];
                    if (!double.IsNaN(conv) && conv > 0)
                        conversions[compName] = conv;
                }
            }
        }
        catch { /* ComponentConversions may not be populated */ }

        // Heat duty
        double? heatDuty = null;
        try { heatDuty = (double?)reactor.DeltaQ; }
        catch { /* ignore */ }

        // Residence time (for CSTR)
        double? residenceTime = null;
        try
        {
            if (req.ReactorType.Equals("CSTR", StringComparison.OrdinalIgnoreCase))
                residenceTime = (double?)reactor.ResidenceTimeL;
            else
                residenceTime = (double?)reactor.ResidenceTime;
        }
        catch { /* ignore */ }

        // PFR profiles
        ReactorProfilesResult? profiles = null;
        if (req.ReactorType.Equals("PFR", StringComparison.OrdinalIgnoreCase))
        {
            profiles = ExtractPfrProfiles(reactor, req);
        }

        return new ReactorSimulationResponse(
            Status: "success",
            OutletStream: outletStream,
            Conversions: conversions.Count > 0 ? conversions : null,
            HeatDuty: heatDuty,
            ResidenceTime: residenceTime,
            Profiles: profiles,
            TransientProfiles: transientProfiles,
            Errors: new List<string>(),
            Warnings: warnings
        );
    }

    private static (List<string> Errors, ReactorTransientProfilesResult? Profiles) ExecuteTransientCstrSimulation(
        DwsimEngine engine,
        dynamic flowsheet,
        dynamic outlet,
        dynamic reactor,
        ReactorSimulationRequest req,
        List<string> warnings)
    {
        var transient = req.Transient!;
        var requestedTimes = BuildTransientTimeline(transient);
        if (requestedTimes.Count == 0)
            return (new List<string>(), null);

        var time = new List<double>();
        var temperatures = new List<double>();
        var pressures = new List<double>();
        var compositions = req.Compounds.ToDictionary(compound => compound, _ => new List<double>());

        var firstPositiveTime = requestedTimes.FirstOrDefault(t => t > TimeComparisonTolerance);
        if (firstPositiveTime <= TimeComparisonTolerance)
            firstPositiveTime = transient.TimeStep ?? transient.FinalTime ?? 1.0;

        engine.EnsureDynamicConfiguration(flowsheet, TimeSpan.FromSeconds(firstPositiveTime));
        ConfigureDynamicCstrReactor(reactor, transient);

        var currentTime = 0.0;
        foreach (var targetTime in requestedTimes)
        {
            if (Math.Abs(targetTime) <= TimeComparisonTolerance)
            {
                if (transient.InitializeFromInlet)
                {
                    AppendInitialTransientSample(req, time, temperatures, pressures, compositions);
                }
                else
                {
                    warnings.Add("Skipped transient sample at t=0 because InitializeFromInlet is false.");
                }
                continue;
            }

            var step = targetTime - currentTime;
            if (step <= TimeComparisonTolerance)
                continue;

            engine.ConfigureDynamicStep(
                flowsheet,
                TimeSpan.FromSeconds(currentTime),
                TimeSpan.FromSeconds(step));

            var errors = engine.Solve(flowsheet, req.TimeoutSeconds);
            if (errors.Count > 0)
                return (errors, null);

            AppendTransientOutletSample(outlet, req, targetTime, time, temperatures, pressures, compositions);
            currentTime = targetTime;
        }

        if (time.Count == 0)
            return (new List<string>(), null);

        return (new List<string>(), new ReactorTransientProfilesResult(
            Time: time,
            Temperature: temperatures,
            Pressure: pressures,
            Compositions: compositions
        ));
    }

    private static void ConfigureDynamicCstrReactor(
        dynamic reactor,
        ReactorTransientSettingsRequest transient)
    {
        reactor.SetDynamicProperty("Initialize using Inlet Stream", transient.InitializeFromInlet);
        reactor.SetDynamicProperty("Reset Contents", transient.ResetContents);
    }

    private static void AppendInitialTransientSample(
        ReactorSimulationRequest req,
        List<double> time,
        List<double> temperatures,
        List<double> pressures,
        Dictionary<string, List<double>> compositions)
    {
        var inlet = req.InletStreams[0];
        time.Add(0.0);
        temperatures.Add(inlet.Temperature);
        pressures.Add(inlet.Pressure);

        foreach (var compound in req.Compounds)
        {
            var value = inlet.Composition?.GetValueOrDefault(compound, 0.0) ?? 0.0;
            compositions[compound].Add(value);
        }
    }

    private static void AppendTransientOutletSample(
        dynamic outlet,
        ReactorSimulationRequest req,
        double targetTime,
        List<double> time,
        List<double> temperatures,
        List<double> pressures,
        Dictionary<string, List<double>> compositions)
    {
        time.Add(targetTime);
        temperatures.Add(DwsimEngine.TryGetDouble(outlet, "PROP_MS_0"));
        pressures.Add(DwsimEngine.TryGetDouble(outlet, "PROP_MS_1"));

        var outletComposition = DwsimEngine.GetPhaseComposition(outlet, req.Compounds, 102);
        foreach (var compound in req.Compounds)
            compositions[compound].Add(outletComposition.GetValueOrDefault(compound, 0.0));
    }

    private static void ValidateTransientSettings(ReactorTransientSettingsRequest? transient)
    {
        if (transient == null)
            throw ValidationError(
                "Transient settings are required when SimulationMode is dynamic.",
                "reactor_transient_settings_required",
                "Provide the transient object when simulationMode is dynamic.");

        var hasTimeGrid = transient.TimeGrid is { Count: > 0 };
        var hasTimeStep = transient.TimeStep is > 0;
        var hasNumberOfPoints = transient.NumberOfPoints is > 1;

        var configuredAxes = (hasTimeGrid ? 1 : 0) + (hasTimeStep ? 1 : 0) + (hasNumberOfPoints ? 1 : 0);
        if (configuredAxes != 1)
            throw ValidationError(
                "Transient settings must specify exactly one of TimeGrid, TimeStep, or NumberOfPoints.",
                "reactor_transient_strategy_ambiguous",
                "Specify exactly one of transient.timeGrid, transient.timeStep, or transient.numberOfPoints.");

        if (hasTimeGrid)
        {
            if (transient.TimeGrid!.Count > MaxTransientOutputPoints)
                throw ValidationError(
                    $"Transient TimeGrid may contain at most {MaxTransientOutputPoints} output points.",
                    "reactor_transient_too_many_points",
                    $"Reduce transient.timeGrid to {MaxTransientOutputPoints} points or fewer.",
                    "Use a coarser output grid for long dynamic runs.");

            for (var i = 0; i < transient.TimeGrid!.Count; i++)
            {
                var t = transient.TimeGrid[i];
                if (t < 0)
                    throw ValidationError(
                        "Transient TimeGrid values must be non-negative.",
                        "reactor_transient_timegrid_negative",
                        "Ensure all transient.timeGrid values are greater than or equal to zero.");

                if (i > 0 && t <= transient.TimeGrid[i - 1])
                    throw ValidationError(
                        "Transient TimeGrid values must be strictly increasing.",
                        "reactor_transient_timegrid_not_increasing",
                        "Provide transient.timeGrid values in strictly increasing order.");
            }

            return;
        }

        if (transient.FinalTime is null or <= 0)
            throw ValidationError(
                "Transient FinalTime must be positive when TimeGrid is not provided.",
                "reactor_transient_final_time_invalid",
                "Provide a positive transient.finalTime when using timeStep or numberOfPoints.");

        if (hasTimeStep)
        {
            if (transient.TimeStep!.Value < MinTransientTimeStepSeconds)
                throw ValidationError(
                    $"Transient TimeStep must be at least {MinTransientTimeStepSeconds:0.###} seconds.",
                    "reactor_transient_timestep_too_small",
                    $"Use transient.timeStep >= {MinTransientTimeStepSeconds:0.###} seconds.",
                    "If you need more detail, prefer a shorter finalTime over an extremely small timeStep.");

            var estimatedPointCount = (int)Math.Ceiling(transient.FinalTime.Value / transient.TimeStep.Value) + 1;
            if (estimatedPointCount > MaxTransientOutputPoints)
                throw ValidationError(
                    $"Transient configuration would produce {estimatedPointCount} output points, exceeding the limit of {MaxTransientOutputPoints}.",
                    "reactor_transient_too_many_points",
                    $"Increase transient.timeStep or reduce transient.finalTime so the output contains at most {MaxTransientOutputPoints} points.",
                    "Use transient.numberOfPoints for a coarser uniform output grid when possible.");

            return;
        }

        if (hasNumberOfPoints && transient.NumberOfPoints!.Value > MaxTransientOutputPoints)
            throw ValidationError(
                $"Transient NumberOfPoints must be at most {MaxTransientOutputPoints}.",
                "reactor_transient_too_many_points",
                $"Use transient.numberOfPoints <= {MaxTransientOutputPoints}.",
                "Use a coarser output grid for long dynamic runs.");
    }

    private static List<double> BuildTransientTimeline(ReactorTransientSettingsRequest transient)
    {
        if (transient.TimeGrid is { Count: > 0 })
            return transient.TimeGrid.ToList();

        var finalTime = transient.FinalTime!.Value;
        var result = new List<double> { 0.0 };

        if (transient.TimeStep is > 0)
        {
            var timeStep = transient.TimeStep.Value;
            for (var current = timeStep; current < finalTime - TimeComparisonTolerance; current += timeStep)
                result.Add(current);

            if (Math.Abs(result[^1] - finalTime) > TimeComparisonTolerance)
                result.Add(finalTime);

            return result;
        }

        var numberOfPoints = transient.NumberOfPoints!.Value;
        if (numberOfPoints == 2)
        {
            result.Add(finalTime);
            return result;
        }

        var increment = finalTime / (numberOfPoints - 1);
        for (var i = 1; i < numberOfPoints; i++)
            result.Add(i * increment);

        result[^1] = finalTime;
        return result;
    }

    private static bool IsDynamicSimulation(ReactorSimulationRequest req) =>
        NormalizeSimulationMode(req.SimulationMode) == "dynamic";

    private static string NormalizeSimulationMode(string simulationMode) =>
        NormalizeEnumToken(simulationMode) switch
        {
            "steady_state" or "steadystate" => "steady_state",
            "dynamic" or "transient" => "dynamic",
            _ => throw ValidationError(
                $"Unsupported simulation mode: {simulationMode}. Supported: steady_state, dynamic.",
                "reactor_simulation_mode_unsupported",
                "Use simulationMode=\"steady_state\" or simulationMode=\"dynamic\".")
        };

    private static string NormalizeThermalMode(string thermalMode) =>
        NormalizeEnumToken(thermalMode) switch
        {
            "isothermal" or "isothermic" => "isothermal",
            "adiabatic" => "adiabatic",
            "outlet_temperature" or "outlettemperature" => "outlet_temperature",
            "defined_duty" or "specified_duty" or "nonisothermal_nonadiabatic" => "defined_duty",
            _ => throw ValidationError(
                $"Unsupported thermal mode: {thermalMode}. Supported: isothermal, adiabatic, outlet_temperature, defined_duty.",
                "reactor_thermal_mode_unsupported",
                "Use thermalMode=\"isothermal\", \"adiabatic\", \"outlet_temperature\", or \"defined_duty\".")
        };

    private static string NormalizeEnumToken(string value) =>
        value.Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');

    private static ApiValidationException ValidationError(
        string message,
        string code,
        params string[] suggestions) =>
        new(message, code, suggestions);

    private static ReactorProfilesResult? ExtractPfrProfiles(
        dynamic reactor, ReactorSimulationRequest req)
    {
        try
        {
            var points = reactor.Profile;
            if (points == null) return null;

            var positions = new List<double>();
            var temperatures = new List<double>();
            var compositions = new Dictionary<string, List<double>>();

            foreach (var compound in req.Compounds)
                compositions[compound] = new List<double>();

            // PFR Profile is a dictionary of property→list of values along reactor length
            // Access pattern depends on DWSIM version; try common patterns
            int count = 0;
            try
            {
                // Try to read "T" profile for temperature
                if (points.ContainsKey("T"))
                {
                    foreach (double t in points["T"])
                    {
                        temperatures.Add(t);
                        count++;
                    }
                }
            }
            catch { /* Profile format may differ */ }

            if (count == 0) return null;

            // Generate position values
            double length = DwsimEngine.TryGetDouble(reactor, "Length", 1.0);
            for (int i = 0; i < count; i++)
                positions.Add(length * i / Math.Max(count - 1, 1));

            // Try to read composition profiles
            foreach (var compound in req.Compounds)
            {
                try
                {
                    if (points.ContainsKey(compound))
                    {
                        foreach (double v in points[compound])
                            compositions[compound].Add(v);
                    }
                }
                catch { /* ignore */ }
            }

            return new ReactorProfilesResult(
                Position: positions,
                Temperature: temperatures,
                Compositions: compositions
            );
        }
        catch
        {
            return null;
        }
    }
}
