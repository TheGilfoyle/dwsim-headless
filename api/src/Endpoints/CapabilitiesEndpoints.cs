using DwsimService.Services;

namespace DwsimService.Endpoints;

public static class CapabilitiesEndpoints
{
    public static void MapCapabilitiesEndpoints(this WebApplication app)
    {
        app.MapGet("/capabilities", async (DwsimEnginePool pool, CancellationToken ct) =>
        {
            var version = await pool.ExecuteAsync(engine => engine.GetVersion(), ct);
            return Results.Ok(new
            {
                name = "DWSIM",
                version,
                capabilities = new[]
                {
                    "compounds",
                    "property_packages",
                    "thermodynamic",
                    "transport",
                    "surface",
                    "flash",
                    "reactor",
                    "verify_compounds"
                },
                description = "DWSIM-based thermodynamic property calculations, flash equilibrium, and reactor simulations",
                reactorSupport = new
                {
                    reactorTypes = new[] { "CSTR", "PFR" },
                    simulationModes = new
                    {
                        steadyState = new
                        {
                            supportedReactorTypes = new[] { "CSTR", "PFR" },
                            supportedThermalModes = new[] { "isothermal", "adiabatic", "outlet_temperature", "defined_duty" }
                        },
                        dynamic = new
                        {
                            supportedReactorTypes = new[] { "CSTR" },
                            supportedThermalModes = new[] { "adiabatic", "defined_duty" },
                            constraints = new[]
                            {
                                "Dynamic simulation is currently supported only for CSTR.",
                                "Dynamic CSTR does not support isothermal or outlet_temperature thermal modes."
                            }
                        }
                    }
                }
            });
        });
    }
}
