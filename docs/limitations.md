# Limitations and Known Issues

## Functional Limitations

### Disabled Functionality (Wrapped in #If Not HEADLESS)

| Functionality | Reason | Impact |
|---------------|--------|--------|
| **Editing Forms** (EditingForms) | Depend on System.Windows.Forms | Cannot visually edit unit operation properties. All properties are accessible programmatically via `SetPropertyValue()` |
| **Python.NET Scripts** (PythonScriptUO) | Depends on Python.Runtime.dll for Windows | `PythonScriptUO` cannot be used to execute custom Python scripts inside DWSIM. Workaround: perform calculations in Python and pass results via the API |
| **CapeOpen UI** (CustomUO_CO.Edit) | Depends on WinForms forms | CapeOpen unit operations work, but the `Edit()` method is unavailable. Configuration is done through the programmatic API |
| **ChangeCalculationOrder** | Uses FormCustomCalcOrder | In headless mode, simply returns the current order unchanged. Calculation order is determined automatically by the solver |
| **NetOffice Excel COM** (Spreadsheet.vb) | COM Automation for Excel on Windows | Reading/writing Excel via COM is unavailable. Data can be passed through Python |
| **DynamicsPropertyEditor** | WinForms editor | Dynamic properties are accessible programmatically |

### IronPython Scripts

IronPython (`RunScript_IronPython`) **works**. Only Python.NET (`RunScript_PythonNET`) is disabled. If your scripts are written for IronPython, they will work.

### Dynamic CSTR Runtime Limits

Dynamic `CSTR` simulation currently advances the flowsheet sequentially for each requested output time.
To keep API latency and resource usage predictable, the reactor endpoint applies these guardrails:

- maximum `500` transient output points per request
- minimum `timeStep` of `0.1` seconds

These limits apply whether output times are provided explicitly via `timeGrid`, requested through `numberOfPoints`, or derived from `finalTime / timeStep`.
For longer horizons, prefer a coarser output grid and post-process or resample results client-side if needed.

### SkiaSharp Drawing

Flowsheet drawing via SkiaSharp **works** -- `CreateFlowsheet()` creates a fully functional `Flowsheet2` with drawing support. However, for SkiaSharp to work correctly on Linux, the following are required:
- `libfontconfig1` and `fonts-dejavu-core` (installed in the Docker image)
- The native library `libSkiaSharp.so` for the correct architecture (copied automatically)

## Compatibility

### NuGet Dependencies

Some NuGet packages do not have native .NET 8 support and are used in compatibility mode:

| Package | Warning | Impact |
|---------|---------|--------|
| GemBox.Spreadsheet 39.3.30.1215 | NU1701: restored using .NET Framework | May not work fully. Used for exporting results to spreadsheets |
| iTextSharp-LGPL 4.1.6 | NU1701: restored using .NET Framework | May not work fully. Used for PDF export |

### Processor Architecture

The Docker image is built for the host machine's architecture:
- **x86_64** (Intel/AMD) -- fully supported
- **aarch64** (Apple Silicon, ARM) -- fully supported

The SkiaSharp native library is automatically selected for the current architecture in `scripts/build.sh`.

### Build Warnings

The build completes with **362 warnings** (0 errors). Main categories:

| Category | Count | Description |
|----------|-------|-------------|
| CA1416 | ~100 | Platform-specific API (System.Drawing, FontConverter) -- these APIs are only called in Windows code paths |
| BC42016/BC42020 | ~150 | VB.NET implicit conversions -- the original code's style |
| SYSLIB0011 | ~20 | BinaryFormatter obsolete -- used for backward compatibility with old files |
| CS0618 | ~10 | SkiaSharp deprecated API -- GRBackendRenderTargetDesc |
| NU1701 | ~5 | .NET Framework package compatibility |

These warnings **do not affect** functionality for headless use.

## What to Do If...

### You Need a Unit Operation That Has No Editing Form

All unit operations **work**. Forms are only needed for visual editing. Programmatically:

```python
# Create a unit operation
heater = fs.add_unit_operation("Heater", "HTR-001")

# Set properties via codes
heater.set_property("PROP_HT_0", 400.0)  # Outlet temperature
heater.set_property("PROP_HT_1", 0.0)    # Pressure drop
```

Property codes for each unit operation are documented on the [DWSIM Wiki](https://dwsim.org/wiki/index.php?title=Object_Property_Codes).

### You Need a Calculation Order Different from the Automatic One

The DWSIM solver automatically determines the calculation order of unit operations. In headless mode, `ChangeCalculationOrder()` returns the current order unchanged (there is no UI dialog for manual reordering). Typically, the automatic order works correctly.

### libSkiaSharp.so Loading Error

If calling `CreateFlowsheet()` raises `DllNotFoundException: libSkiaSharp`:
1. Check that the native library is copied: `ls /app/dwsim/libSkiaSharp.so`
2. Check the architecture: `file /app/dwsim/libSkiaSharp.so` should match `uname -m`
3. Check dependencies: `apt-get install libfontconfig1 fonts-dejavu-core`

### You Need a Newer Version of DWSIM

The fork's `headless` branch is based on a specific upstream commit. When updating to a newer DWSIM version, rebase the `headless` branch onto the new upstream commit:

```bash
git fetch upstream
git rebase upstream/windows
```

The rebase may require conflict resolution if:
- New files with WinForms code were added
- Signatures of wrapped methods were changed
- New dependencies were added

It is recommended to run a full build and verify the smoke test after updating.
