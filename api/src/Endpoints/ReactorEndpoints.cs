using DwsimService.Models.Requests;
using DwsimService.Services;
using DwsimService.Infrastructure;

namespace DwsimService.Endpoints;

public static class ReactorEndpoints
{
    public static void MapReactorEndpoints(this WebApplication app)
    {
        app.MapPost("/reactor/simulate",
            async (ReactorSimulationRequest req, DwsimEnginePool pool, CancellationToken ct) =>
        {
            try
            {
                var result = await pool.ExecuteAsync(engine =>
                    ReactorSimulationService.Simulate(engine, req), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return ApiErrorResults.BadRequest(ex, "invalid_reactor_request");
            }
            catch (Exception ex)
            {
                return ApiErrorResults.UnprocessableEntity(
                    "Reactor simulation failed",
                    ex,
                    "reactor_simulation_failed");
            }
        });
    }
}
