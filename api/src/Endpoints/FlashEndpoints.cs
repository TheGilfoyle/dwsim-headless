using DwsimService.Models.Requests;
using DwsimService.Models.Responses;
using DwsimService.Services;
using DwsimService.Infrastructure;

namespace DwsimService.Endpoints;

public static class FlashEndpoints
{
    public static void MapFlashEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/flash");

        group.MapPost("/pt", async (FlashRequest req, DwsimEnginePool pool, CancellationToken ct) =>
            await ExecuteFlash(req, "pt", pool, ct));

        group.MapPost("/ph", async (FlashRequest req, DwsimEnginePool pool, CancellationToken ct) =>
            await ExecuteFlash(req, "ph", pool, ct));

        group.MapPost("/ps", async (FlashRequest req, DwsimEnginePool pool, CancellationToken ct) =>
            await ExecuteFlash(req, "ps", pool, ct));

        group.MapPost("/tvf", async (FlashRequest req, DwsimEnginePool pool, CancellationToken ct) =>
            await ExecuteFlash(req, "tvf", pool, ct));

        group.MapPost("/pvf", async (FlashRequest req, DwsimEnginePool pool, CancellationToken ct) =>
            await ExecuteFlash(req, "pvf", pool, ct));
    }

    private static async Task<IResult> ExecuteFlash(
        FlashRequest req, string flashType, DwsimEnginePool pool, CancellationToken ct)
    {
        try
        {
            var result = await pool.ExecuteAsync(engine =>
                CalculateFlash(engine, req, flashType), ct);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return ApiErrorResults.BadRequest(ex, "invalid_flash_request");
        }
        catch (Exception ex)
        {
            return ApiErrorResults.UnprocessableEntity(
                "Flash calculation failed",
                ex,
                "flash_calculation_failed");
        }
    }

    private static FlashResponse CalculateFlash(
        DwsimEngine engine, FlashRequest req, string flashType)
    {
        var fs = engine.CreateConfiguredFlowsheet(req.Compounds, req.PropertyPackage);

        var stream = DwsimEngine.AddObjectToFlowsheet(fs, "MaterialStream", 0, 0, "FLASH_STREAM");

        switch (flashType)
        {
            case "pt":
                stream.SetPropertyValue("PROP_MS_0", req.Temperature!.Value);   // Temperature
                stream.SetPropertyValue("PROP_MS_1", req.Pressure!.Value);      // Pressure
                break;
            case "ph":
                stream.SetPropertyValue("PROP_MS_1", req.Pressure!.Value);      // Pressure
                stream.SetPropertyValue("PROP_MS_7", req.Enthalpy!.Value);      // Specific Enthalpy
                break;
            case "ps":
                stream.SetPropertyValue("PROP_MS_1", req.Pressure!.Value);      // Pressure
                stream.SetPropertyValue("PROP_MS_8", req.Entropy!.Value);       // Specific Entropy
                break;
            case "tvf":
                stream.SetPropertyValue("PROP_MS_0", req.Temperature!.Value);   // Temperature
                stream.SetPropertyValue("PROP_MS_27", req.VaporFraction!.Value); // Vapor fraction
                break;
            case "pvf":
                stream.SetPropertyValue("PROP_MS_1", req.Pressure!.Value);      // Pressure
                stream.SetPropertyValue("PROP_MS_27", req.VaporFraction!.Value); // Vapor fraction
                break;
        }

        stream.SetPropertyValue("PROP_MS_3", 1.0);  // Molar Flow (mol/s)

        DwsimEngine.SetStreamComposition(stream, req.Compounds, req.Composition);

        var errors = engine.Solve(fs);

        double vaporFraction = DwsimEngine.TryGetDouble(stream, "PROP_MS_27");

        var phases = new List<PhaseResult>();
        if (vaporFraction > 0.0001)
            phases.Add(ExtractPhaseResult(stream, "Vapor", req.Compounds));
        if (vaporFraction < 0.9999)
            phases.Add(ExtractPhaseResult(stream, "Liquid", req.Compounds));

        return new FlashResponse(
            Temperature: DwsimEngine.TryGetDouble(stream, "PROP_MS_0"),
            Pressure: DwsimEngine.TryGetDouble(stream, "PROP_MS_1"),
            VaporFraction: vaporFraction,
            Phases: phases,
            Errors: errors
        );
    }

    private static PhaseResult ExtractPhaseResult(
        dynamic stream, string phaseName, List<string> compounds)
    {
        // Compound-level mole fractions per phase:
        // 106 = Vapor phase, 108 = Liquid phase 1
        int phaseCode = phaseName == "Vapor" ? 106 : 108;
        var composition = DwsimEngine.GetPhaseComposition(stream, compounds, phaseCode);

        double vf = DwsimEngine.TryGetDouble(stream, "PROP_MS_27");

        // Phase-specific enthalpy/entropy:
        // Vapor: PROP_MS_16 (Molar Enthalpy), PROP_MS_17 (Molar Entropy)
        // Liquid: PROP_MS_34 (Molar Enthalpy), PROP_MS_35 (Molar Entropy)
        double? enthalpy, entropy;
        if (phaseName == "Vapor")
        {
            enthalpy = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_16");
            entropy = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_17");
        }
        else
        {
            enthalpy = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_34");
            entropy = DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_35");
        }

        return new PhaseResult(
            PhaseName: phaseName,
            Fraction: phaseName == "Vapor" ? vf : 1.0 - vf,
            Composition: composition,
            Temperature: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_0"),
            Pressure: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_1"),
            Enthalpy: enthalpy,
            Entropy: entropy,
            MolarVolume: null
        );
    }
}
