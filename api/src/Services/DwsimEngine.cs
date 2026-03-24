using System.Reflection;
using Microsoft.Extensions.Logging;

namespace DwsimService.Services;

/// <summary>
/// Wraps a single DWSIM Automation3 instance.
/// NOT thread-safe — use via DwsimEnginePool.
/// </summary>
public class DwsimEngine : IDisposable
{
    private const string HeadlessIntegratorId = "headless-integrator";
    private const string HeadlessScheduleId = "headless-schedule";

    private readonly dynamic _auto;
    private readonly ILogger<DwsimEngine> _logger;
    private static bool _assembliesLoaded;
    private static readonly object _loadLock = new();

    public DwsimEngine(string dllPath, ILogger<DwsimEngine> logger)
    {
        _logger = logger;
        LoadAssemblies(dllPath);

        var automationType = Assembly.Load("DWSIM.Automation")
            .GetType("DWSIM.Automation.Automation3")!;
        _auto = Activator.CreateInstance(automationType)!;
    }

    private static void LoadAssemblies(string dllPath)
    {
        lock (_loadLock)
        {
            if (_assembliesLoaded) return;

            // Register resolver for transitive dependencies
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                var path = Path.Combine(dllPath, $"{name}.dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            var assemblyNames = new[]
            {
                "DWSIM.Automation",
                "DWSIM.GlobalSettings",
                "DWSIM.Interfaces",
                "DWSIM.Thermodynamics",
                "DWSIM.UnitOperations",
                "DWSIM.FlowsheetBase",
                "DWSIM.FlowsheetSolver",
                "DWSIM.SharedClasses",
                "DWSIM.DynamicsManager",
            };

            foreach (var name in assemblyNames)
            {
                var path = Path.Combine(dllPath, $"{name}.dll");
                if (File.Exists(path))
                    Assembly.LoadFrom(path);
            }

            _assembliesLoaded = true;
        }
    }

    /// <summary>Create a new empty flowsheet with compounds and property package.</summary>
    public dynamic CreateConfiguredFlowsheet(
        IEnumerable<string> compounds,
        string propertyPackage)
    {
        var fs = _auto.CreateFlowsheet();

        foreach (var compound in compounds)
            fs.AddCompound(compound);

        fs.CreateAndAddPropertyPackage(propertyPackage);

        return fs;
    }

    /// <summary>Get DWSIM version string.</summary>
    public string GetVersion() => _auto.GetVersion().ToString();

    /// <summary>Get all available compound names.</summary>
    public List<string> GetAvailableCompounds()
    {
        var compounds = _auto.AvailableCompounds;
        var result = new List<string>();
        foreach (var key in compounds.Keys)
            result.Add(key.ToString()!);
        return result;
    }

    /// <summary>Get all available property package names.</summary>
    public List<string> GetAvailablePropertyPackages()
    {
        var packages = _auto.AvailablePropertyPackages;
        var result = new List<string>();
        foreach (var key in packages.Keys)
            result.Add(key.ToString()!);
        return result;
    }

    /// <summary>Solve a flowsheet and return errors if any.</summary>
    /// <remarks>
    /// Automation3.CalculateFlowsheet3 (DispId 7) is void, while the explicit
    /// interface implementation returns List&lt;Exception&gt;. Dynamic dispatch
    /// resolves to the public void overload, causing a RuntimeBinderException.
    /// Workaround: set SolverTimeoutSeconds via reflection and use CalculateFlowsheet4.
    /// </remarks>
    public List<string> Solve(dynamic flowsheet, int? timeoutSeconds = null)
    {
        if (timeoutSeconds.HasValue)
        {
            var settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == "DWSIM.GlobalSettings.Settings");
            settingsType?.GetProperty("SolverTimeoutSeconds",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, timeoutSeconds.Value);
        }

        dynamic? exceptions = _auto.CalculateFlowsheet4(flowsheet);

        var errors = new List<string>();
        if (exceptions != null)
        {
            foreach (var ex in exceptions)
                errors.Add(ex.Message.ToString());
        }
        return errors;
    }

    /// <summary>
    /// Enable DWSIM dynamic mode and ensure a minimal integrator/schedule configuration
    /// exists for headless step-by-step execution.
    /// </summary>
    public void EnsureDynamicConfiguration(dynamic flowsheet, TimeSpan initialStep)
    {
        dynamic manager = flowsheet.DynamicsManager;
        dynamic integrators = manager.IntegratorList;
        dynamic schedules = manager.ScheduleList;

        if (!integrators.ContainsKey(HeadlessIntegratorId))
        {
            dynamic integrator = CreateDynamicsObject("DWSIM.DynamicsManager.Integrator");
            integrator.ID = HeadlessIntegratorId;
            integrator.Description = "Headless Integrator";
            integrator.IntegrationStep = initialStep;
            integrator.Duration = initialStep;
            integrator.CalculationRateEquilibrium = 1;
            integrator.CalculationRatePressureFlow = 1;
            integrator.CalculationRateControl = 1;
            integrator.RealTime = false;
            integrators.Add(HeadlessIntegratorId, integrator);
        }

        if (!schedules.ContainsKey(HeadlessScheduleId))
        {
            dynamic schedule = CreateDynamicsObject("DWSIM.DynamicsManager.Schedule");
            schedule.ID = HeadlessScheduleId;
            schedule.Description = "Headless Schedule";
            schedule.CurrentIntegrator = HeadlessIntegratorId;
            schedule.UseCurrentStateAsInitial = true;
            schedule.ResetContentsOfAllObjects = false;
            schedules.Add(HeadlessScheduleId, schedule);
        }

        manager.CurrentSchedule = HeadlessScheduleId;
        flowsheet.DynamicMode = true;

        try
        {
            flowsheet.FlowsheetOptions.DynamicModeEnabled = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not enable FlowsheetOptions.DynamicModeEnabled explicitly. Falling back to flowsheet.DynamicMode only.");
        }
    }

    /// <summary>
    /// Configure the current dynamic step before solving the flowsheet.
    /// </summary>
    public void ConfigureDynamicStep(
        dynamic flowsheet,
        TimeSpan currentTime,
        TimeSpan integrationStep,
        bool calculateEquilibrium = true,
        bool calculatePressureFlow = true,
        bool calculateControl = false)
    {
        EnsureDynamicConfiguration(flowsheet, integrationStep);

        dynamic manager = flowsheet.DynamicsManager;
        dynamic schedule = manager.ScheduleList[HeadlessScheduleId];
        dynamic integrator = manager.IntegratorList[HeadlessIntegratorId];

        schedule.CurrentIntegrator = HeadlessIntegratorId;
        manager.CurrentSchedule = HeadlessScheduleId;

        integrator.CurrentTime = DateTime.MinValue.Add(currentTime);
        integrator.IntegrationStep = integrationStep;
        integrator.Duration = integrationStep;
        integrator.ShouldCalculateEquilibrium = calculateEquilibrium;
        integrator.ShouldCalculatePressureFlow = calculatePressureFlow;
        integrator.ShouldCalculateControl = calculateControl;
    }

    private static dynamic CreateDynamicsObject(string typeName)
    {
        var type = Assembly.Load("DWSIM.DynamicsManager").GetType(typeName)
                   ?? throw new InvalidOperationException($"Type not found: {typeName}");
        return Activator.CreateInstance(type)
               ?? throw new InvalidOperationException($"Could not create instance of {typeName}");
    }

    /// <summary>Get the ObjectType enum value by name.</summary>
    public static object GetObjectType(string name)
    {
        var enumType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .First(t => t.FullName == "DWSIM.Interfaces.Enums.GraphicObjects.ObjectType");
        return Enum.Parse(enumType, name);
    }

    /// <summary>
    /// Add a simulation object to the flowsheet using reflection.
    /// Dynamic dispatch cannot resolve the overload when ObjectType is boxed as object,
    /// so we invoke AddObject via reflection with the correctly-typed enum parameter.
    /// </summary>
    public static dynamic AddObjectToFlowsheet(dynamic flowsheet, string objectTypeName, int x, int y, string tag)
    {
        var objType = GetObjectType(objectTypeName);
        var fsType = ((object)flowsheet).GetType();
        var method = fsType.GetMethod("AddObject", new[] { objType.GetType(), typeof(int), typeof(int), typeof(string) });
        if (method == null)
            throw new InvalidOperationException($"AddObject method not found on {fsType.FullName}");
        return method.Invoke(flowsheet, new object[] { objType, x, y, tag })!;
    }

    /// <summary>Safely get a double property value.</summary>
    public static double TryGetDouble(dynamic obj, string prop, double fallback = 0.0)
    {
        try { return (double)obj.GetPropertyValue(prop); }
        catch { return fallback; }
    }

    /// <summary>Safely get a nullable double property value.</summary>
    public static double? TryGetNullableDouble(dynamic obj, string prop)
    {
        try
        {
            double val = (double)obj.GetPropertyValue(prop);
            return double.IsNaN(val) || double.IsInfinity(val) ? null : val;
        }
        catch { return null; }
    }

    /// <summary>
    /// Set overall mole-fraction composition on a material stream.
    /// Uses PROP_MS_102/CompoundName format required by DWSIM.
    /// </summary>
    public static void SetStreamComposition(dynamic stream, List<string> compounds, List<double> composition)
    {
        for (int i = 0; i < compounds.Count; i++)
            stream.SetPropertyValue($"PROP_MS_102/{compounds[i]}", composition[i]);
    }

    /// <summary>
    /// Get per-compound mole fractions for a specific phase.
    /// Phase codes: 102=Overall, 106=Vapor, 107=LiquidMix, 108=Liquid1, 109=Liquid2.
    /// </summary>
    public static Dictionary<string, double> GetPhaseComposition(
        dynamic stream, List<string> compounds, int phaseCode)
    {
        var result = new Dictionary<string, double>();
        foreach (var compound in compounds)
        {
            try
            {
                double val = (double)stream.GetPropertyValue($"PROP_MS_{phaseCode}/{compound}");
                result[compound] = double.IsNaN(val) || double.IsInfinity(val) ? 0.0 : val;
            }
            catch { result[compound] = 0.0; }
        }
        return result;
    }

    public void Dispose()
    {
        try { _auto.ReleaseResources(); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }
}
