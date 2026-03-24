using DwsimService.Models.Requests;
using DwsimService.Models.Responses;
using DwsimService.Services;
using DwsimService.Infrastructure;

namespace DwsimService.Endpoints;

public static class TransportEndpoints
{
    public static void MapTransportEndpoints(this WebApplication app)
    {
        app.MapPost("/properties/transport",
            async (TransportRequest req, DwsimEnginePool pool, CancellationToken ct) =>
        {
            try
            {
                var result = await pool.ExecuteAsync(engine =>
                    CalculateTransport(engine, req), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return ApiErrorResults.BadRequest(ex, "invalid_transport_request");
            }
            catch (Exception ex)
            {
                return ApiErrorResults.UnprocessableEntity(
                    "Calculation failed",
                    ex,
                    "transport_calculation_failed");
            }
        });
    }

    private static TransportResponse CalculateTransport(DwsimEngine engine, TransportRequest req)
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

        return new TransportResponse(
            Temperature: (double)stream.GetPropertyValue("PROP_MS_0"),
            Pressure: (double)stream.GetPropertyValue("PROP_MS_1"),
            // PROP_MS_38 = Liquid Phase (Mixture) Dynamic Viscosity (Pa·s)
            ViscosityLiquid: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_38"),
            // PROP_MS_20 = Vapor Phase Dynamic Viscosity (Pa·s)
            ViscosityVapor: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_20"),
            // PROP_MS_36 = Liquid Phase (Mixture) Thermal Conductivity (W/(m·K))
            ThermalConductivityLiquid: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_36"),
            // PROP_MS_18 = Vapor Phase Thermal Conductivity (W/(m·K))
            ThermalConductivityVapor: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_18"),
            // PROP_MS_11 = Mixture Thermal Conductivity (W/(m·K))
            ThermalConductivityMixture: DwsimEngine.TryGetNullableDouble(stream, "PROP_MS_11")
        );
    }
}
