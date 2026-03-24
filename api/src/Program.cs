using DwsimService.Endpoints;
using DwsimService.Services;

var builder = WebApplication.CreateBuilder(args);

var dllPath = Environment.GetEnvironmentVariable("DWSIM_DLL_PATH") ?? "/app/dwsim";
var poolSize = int.TryParse(Environment.GetEnvironmentVariable("DWSIM_POOL_SIZE"), out var ps) ? ps : 4;

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<DwsimEngine>>();
    return new DwsimEnginePool(dllPath, poolSize, logger);
});
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapHealthEndpoints();
app.MapCapabilitiesEndpoints();
app.MapVerifyCompoundsEndpoints();
app.MapCompoundsEndpoints();
app.MapPropertyPackageEndpoints();
app.MapThermodynamicEndpoints();
app.MapTransportEndpoints();
app.MapSurfaceEndpoints();
app.MapFlashEndpoints();
app.MapReactorEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
