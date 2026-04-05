using DwsimService.Models.Requests;
using DwsimService.Models.Responses;
using DwsimService.Services;
using DwsimService.Infrastructure;

namespace DwsimService.Endpoints;

public static class ThermodynamicEndpoints
{
    public static void MapThermodynamicEndpoints(this WebApplication app)
    {
        app.MapPost("/properties/thermodynamic",
            async (ThermodynamicRequest req, DwsimEnginePool pool, CancellationToken ct) =>
        {
            try
            {
                var result = await pool.ExecuteAsync(engine =>
                    CalculateProperties(engine, req), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return ApiErrorResults.BadRequest(ex, "invalid_thermodynamic_request");
            }
            catch (Exception ex)
            {
                return ApiErrorResults.UnprocessableEntity(
                    "Calculation failed",
                    ex,
                    "thermodynamic_calculation_failed");
            }
        });
    }

    private static ThermodynamicResponse CalculateProperties(
        DwsimEngine engine, ThermodynamicRequest req)
    {
        var fs = engine.CreateConfiguredFlowsheet(req.Compounds, req.PropertyPackage);

        var stream = DwsimEngine.AddObjectToFlowsheet(fs, "MaterialStream", 0, 0, "CALC_STREAM");

        stream.SetPropertyValue("PROP_MS_0", req.Temperature);   // Temperature (K)
        stream.SetPropertyValue("PROP_MS_1", req.Pressure);      // Pressure (Pa)
        stream.SetPropertyValue("PROP_MS_3", 1.0);               // Molar Flow (mol/s)

        DwsimEngine.SetStreamComposition(stream, req.Compounds, req.Composition);

        var errors = engine.Solve(fs);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("; ", errors));

        var phases = new List<string>();
        double vf = DwsimEngine.TryGetDouble(stream, "PROP_MS_27");  // Vapor molar fraction
        if (vf > 0.0001) phases.Add("Vapor");
        if (vf < 0.9999) phases.Add("Liquid");
        if (phases.Count == 0) phases.Add("Unknown");

        // PROP_MS_9  = Mixture Molar Enthalpy (J/mol in SI)
        // PROP_MS_10 = Mixture Molar Entropy (J/(mol·K) in SI)
        double enthalpy = DwsimEngine.TryGetDouble(stream, "PROP_MS_9");
        double entropy = DwsimEngine.TryGetDouble(stream, "PROP_MS_10");

        // Molar volume: MW / density (both mixture overall)
        double? molarVolume = null;
        double mw = DwsimEngine.TryGetDouble(stream, "PROP_MS_6");      // Mixture MW (kg/kmol)
        double density = DwsimEngine.TryGetDouble(stream, "PROP_MS_5"); // Mixture density (kg/m³)
        if (mw > 0 && density > 0)
            molarVolume = (mw / 1000.0) / density; // kg/mol ÷ kg/m³ = m³/mol

        // Heat capacities: phase-dependent
        // Vapor: PROP_MS_21 (Cp), Liquid: PROP_MS_39 (Cp)
        // Use weighted average by vapor fraction
        double? heatCapacityCp = null;
        double? heatCapacityCv = null;
        if (vf > 0.9999)
        {
            heatCapacityCp = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_21");
            double? cpCvRatio = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_22");
            if (heatCapacityCp.HasValue && cpCvRatio.HasValue && cpCvRatio.Value > 0)
                heatCapacityCv = heatCapacityCp.Value / cpCvRatio.Value;
        }
        else if (vf < 0.0001)
        {
            heatCapacityCp = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_39");
            double? cpCvRatio = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_40");
            if (heatCapacityCp.HasValue && cpCvRatio.HasValue && cpCvRatio.Value > 0)
                heatCapacityCv = heatCapacityCp.Value / cpCvRatio.Value;
        }
        else
        {
            // Two-phase: report both
            double? cpVap = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_21");
            double? cpLiq = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_39");
            if (cpVap.HasValue && cpLiq.HasValue)
                heatCapacityCp = vf * cpVap.Value + (1 - vf) * cpLiq.Value;
        }

        // Compressibility factor: PROP_MS_26 (Vapor), PROP_MS_44 (Liquid)
        double? compressibilityFactor = vf > 0.5
            ? DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_26")
            : DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_44");

        return new ThermodynamicResponse(
            Temperature: (double)stream.GetPropertyValue("PROP_MS_0"),
            Pressure: (double)stream.GetPropertyValue("PROP_MS_1"),
            MolarVolume: molarVolume,
            Enthalpy: enthalpy,
            Entropy: entropy,
            GibbsEnergy: enthalpy - req.Temperature * entropy,
            HeatCapacityCp: heatCapacityCp,
            HeatCapacityCv: heatCapacityCv,
            MolecularWeight: mw,
            CompressibilityFactor: compressibilityFactor,
            Phases: phases
        );
    }
}
