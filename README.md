# DWSIM Headless Build

A headless build of [DWSIM](https://github.com/DanWBR/dwsim) (open-source chemical process simulator) from source for Linux / Docker with .NET 8. Provides a Python API for programmatic creation and calculation of process flowsheets.

**DWSIM version:** 9.0.5.0 | **1 480 compounds** | **29 property packages** | **31 projects out of ~82**

## Quick Start

```bash
# Clone the repository with the DWSIM submodule
git clone --recursive https://github.com/ShockOfWave/dwsim-headless.git
cd dwsim-headless

# Build the Docker image (~10 min, requires ~10 GB of disk space)
docker build -t dwsim-headless .

# Verify everything works
docker run --rm dwsim-headless
# Output:
# DWSIM Version: DWSIM version 9.0.5.0 (...)
# Compounds: 1480
# Property Packages: 29
```

> **Note:** the `--recursive` flag is required -- it fetches the DWSIM sources as a git submodule. If you have already cloned without it:
> ```bash
> git submodule update --init
> ```

## Example: Flash Separation

```bash
docker run --rm dwsim-headless python3 /app/python/example_flash.py /app/dwsim
```

Or via interactive Python:

```bash
docker run --rm -it dwsim-headless python3
```

```python
from dwsim_client import DWSIMClient

with DWSIMClient("/app/dwsim") as client:
    fs = client.create_flowsheet()

    # Add compounds and a property package
    fs.add_compound("Water")
    fs.add_compound("Ethanol")
    fs.add_property_package("Peng-Robinson (PR)")

    # Create a feed stream: 350 K, 1 atm, 1 kg/s, 50/50 Water/Ethanol
    feed = fs.add_material_stream(
        "FEED", temperature=350.0, pressure=101325.0,
        mass_flow=1.0, composition={"Water": 0.5, "Ethanol": 0.5}
    )

    # Add a flash vessel and outlet streams
    flash = fs.add_unit_operation("Vessel", "FLASH")
    vapor = fs.add_material_stream("VAPOR")
    liquid = fs.add_material_stream("LIQUID")

    # Connect: feed -> flash -> vapor + liquid
    fs.connect(feed, flash, 0, 0)
    fs.connect(flash, vapor, 0, 0)
    fs.connect(flash, liquid, 1, 0)

    # Solve the flowsheet
    errors = fs.solve()
    if not errors:
        print(f"Vapor:  {vapor.mass_flow:.4f} kg/s, {vapor.temperature:.1f} K")
        print(f"Liquid: {liquid.mass_flow:.4f} kg/s, {liquid.temperature:.1f} K")
```

Output:
```
Vapor:  0.8909 kg/s, 350.0 K
Liquid: 0.1091 kg/s, 350.0 K
```

## REST API

A standalone HTTP REST API for thermodynamic property calculations, flash equilibrium, and reactor simulations.

### Quick Start

```bash
# Build the API image (from repo root)
docker build -f api/Dockerfile -t dwsim-api .

# Run
docker run -p 8003:8003 dwsim-api

# Check health
curl http://localhost:8003/health

# List capabilities
curl http://localhost:8003/capabilities
```

### Available Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/capabilities` | GET | Engine capabilities and metadata |
| `/compounds` | GET | List all available compounds |
| `/compounds/search?q=` | GET | Search compounds by name |
| `/property-packages` | GET | List thermodynamic models |
| `/properties/thermodynamic` | POST | Calculate thermodynamic properties |
| `/properties/transport` | POST | Calculate transport properties |
| `/properties/surface` | POST | Calculate surface properties |
| `/flash/{type}` | POST | Flash equilibrium (pt, ph, ps, tvf, pvf) |
| `/reactor/simulate` | POST | Reactor simulation (steady-state PFR/CSTR, dynamic CSTR) |
| `/verify-compounds` | POST | Verify compound availability |

See [docs/api-contract.md](docs/api-contract.md) for the full API specification with request/response schemas.
The reactor API supports steady-state simulations for `PFR` and `CSTR`, plus dynamic `CSTR` runs with transient time-series output.

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_URLS` | `http://+:8003` | Listening address |
| `DWSIM_DLL_PATH` | `/app/dwsim` | Path to DWSIM DLLs |
| `DWSIM_POOL_SIZE` | `4` | Number of engine instances in the pool |

## Requirements

- **Docker** (Docker Desktop or Docker Engine)
- **Git** with submodule support
- **~10 GB** of free disk space
- Internet connection (to download NuGet packages during the build)

## Project Structure

```
├── Dockerfile                  # Multi-stage build (build -> runtime)
├── LICENSE                     # GPL-3.0
├── dwsim/                      # Git submodule: DWSIM sources (ShockOfWave/dwsim, branch headless)
├── api/
│   ├── Dockerfile              # API Docker build (multi-stage)
│   ├── src/                    # ASP.NET Core 8 REST API
│   │   ├── Program.cs          # Entry point
│   │   ├── DwsimService.csproj # Project file
│   │   ├── Services/           # Engine pool and wrappers
│   │   ├── Endpoints/          # HTTP endpoint definitions
│   │   └── Models/             # Request/Response DTOs
│   └── tests/                  # xUnit integration tests
├── scripts/
│   ├── build.sh                # Main build script
│   ├── install.sh              # Bare Linux installation script
│   └── test-docker.sh          # Quick Docker environment test
├── smoke-test/                 # C# smoke tests (6 checks)
├── python/
│   ├── dwsim_client.py         # Python client (wrapper around Automation3)
│   ├── example_flash.py        # Flash separation example
│   └── requirements.txt        # pythonnet>=3.0.3
├── output/                     # Build artifacts (DLL files)
└── docs/
    ├── api-contract.md         # External Engine API contract specification
    ├── architecture.md         # Architecture and description of changes
    ├── approach.md             # Approach and alternatives
    ├── limitations.md          # Limitations and known issues
    └── python-api.md           # Python API reference
```

## How the Build Works

The DWSIM sources are included as a git submodule pointing to the `headless` branch of [`ShockOfWave/dwsim`](https://github.com/ShockOfWave/dwsim). This branch contains all the necessary SDK-style project conversions, WinForms exclusions, and conditional compilation guards already applied.

The build itself consists of three phases:

1. **Restore** -- `dotnet restore` downloads all NuGet dependencies
2. **Build** -- `dotnet build` compiles 31 projects with the `HEADLESS` symbol
3. **Smoke test** -- automated verification (Automation3, CreateFlowsheet, PengRobinson, Mixer, MaterialStream, GlobalSettings)

For details see [docs/architecture.md](docs/architecture.md).

## Bare Linux Installation

If you prefer to run without Docker, use the provided installation script (Ubuntu/Debian):

```bash
git clone --recursive https://github.com/ShockOfWave/dwsim-headless.git
cd dwsim-headless

# Install to /opt/dwsim-headless (requires .NET SDK 8.0)
./scripts/install.sh
```

The script will:
- Install system dependencies (libopenblas, libgfortran5, libfontconfig1, fonts, Python 3)
- Build DWSIM from source
- Copy DLLs and Python files to `/opt/dwsim-headless`
- Create a Python virtual environment with pythonnet

After installation:

```bash
source /opt/dwsim-headless/venv/bin/activate
export PYTHONPATH=/opt/dwsim-headless/python:$PYTHONPATH
python3 -c "from dwsim_client import DWSIMClient; c = DWSIMClient('/opt/dwsim-headless/dwsim'); print(c.version)"
```

You can pass a custom install directory as the first argument: `./scripts/install.sh /path/to/dir`

## Documentation

- [REST API Contract](docs/api-contract.md) -- HTTP API specification for external engine integration
- [Architecture and Changes](docs/architecture.md) -- what was changed and why it works
- [Approach and Alternatives](docs/approach.md) -- why this approach was chosen
- [Limitations](docs/limitations.md) -- what does not work and why
- [Python API](docs/python-api.md) -- Python client reference

## Updating DWSIM

The DWSIM sources live in a fork ([ShockOfWave/dwsim](https://github.com/ShockOfWave/dwsim), branch `headless`) which is included as a git submodule pinned to a specific commit.

To incorporate upstream changes, rebase the `headless` branch onto the latest upstream `windows` branch:

```bash
cd dwsim
git fetch upstream windows
git rebase upstream/windows
cd ..

# Rebuild and verify
docker build -t dwsim-headless .
```

> If you have not added the upstream remote yet:
> ```bash
> cd dwsim
> git remote add upstream https://github.com/DanWBR/dwsim.git
> ```

After a successful rebase, commit the updated submodule pointer in the parent repository.

## Credits

- [DWSIM](https://github.com/DanWBR/dwsim) -- open-source chemical process simulator, created by **Daniel Wagner Oliveira de Medeiros** ([@DanWBR](https://github.com/DanWBR))

## License

This project is distributed under the [GPL-3.0](LICENSE) license, the same as the original DWSIM.

Copyright (C) 2026 Timur Aliev ([@ShockOfWave](https://github.com/ShockOfWave))
