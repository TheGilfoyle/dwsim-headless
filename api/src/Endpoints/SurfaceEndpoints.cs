using DwsimService.Models.Requests;
using DwsimService.Models.Responses;
using DwsimService.Services;
using DwsimService.Infrastructure;

namespace DwsimService.Endpoints;

public static class SurfaceEndpoints
{
    public static void MapSurfaceEndpoints(this WebApplication app)
    {
        app.MapPost("/properties/surface",
            async (SurfaceRequest req, DwsimEnginePool pool, CancellationToken ct) =>
        {
            try
            {
                var result = await pool.ExecuteAsync(engine =>
                    CalculateSurface(engine, req), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return ApiErrorResults.BadRequest(ex, "invalid_surface_request");
            }
            catch (Exception ex)
            {
                return ApiErrorResults.UnprocessableEntity(
                    "Calculation failed",
                    ex,
                    "surface_calculation_failed");
            }
        });
    }

    private static SurfaceResponse CalculateSurface(DwsimEngine engine, SurfaceRequest req)
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

        // Surface tension is not available via PROP_MS codes.
        // Access it directly from the liquid phase properties.
        double? surfaceTension = null;
        try
        {
            surfaceTension = (double?)stream.Phases[0].Properties.surfaceTension;
            if (surfaceTension.HasValue && (double.IsNaN(surfaceTension.Value) || double.IsInfinity(surfaceTension.Value)))
                surfaceTension = null;
        }
        catch { /* phase may not have surface tension */ }

        return new SurfaceResponse(
            Temperature: (double)stream.GetPropertyValue("PROP_MS_0"),
            Pressure: (double)stream.GetPropertyValue("PROP_MS_1"),
            SurfaceTension: surfaceTension
        );
    }
}
