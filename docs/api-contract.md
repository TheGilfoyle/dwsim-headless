# External Engine API Contract

This document specifies the HTTP API contract that any compatible external computation engine must implement. An orchestrator can register engines that follow this contract and transparently proxy user requests to them.

## Required Endpoints

Every engine MUST implement these two endpoints.

### `GET /health`

Returns engine health status.

**Response (200 OK):**
```json
{
  "status": "healthy",
  "version": "8.8.2"
}
```

| Field     | Type   | Required | Description                       |
|-----------|--------|----------|-----------------------------------|
| `status`  | string | yes      | Must be `"healthy"` when operational |
| `version` | string | yes      | Engine software version           |

If the engine is not ready, it may return a non-200 status code.

---

### `GET /capabilities`

Returns engine metadata and the list of supported capabilities.

**Response (200 OK):**
```json
{
  "name": "DWSIM",
  "version": "8.8.2",
  "description": "DWSIM-based thermodynamic calculations",
  "capabilities": [
    "compounds",
    "property_packages",
    "thermodynamic",
    "transport",
    "surface",
    "flash",
    "reactor",
    "verify_compounds"
  ],
  "reactorSupport": {
    "reactorTypes": ["CSTR", "PFR"],
    "simulationModes": {
      "steadyState": {
        "supportedReactorTypes": ["CSTR", "PFR"],
        "supportedThermalModes": ["isothermal", "adiabatic", "outlet_temperature", "defined_duty"]
      },
      "dynamic": {
        "supportedReactorTypes": ["CSTR"],
        "supportedThermalModes": ["adiabatic", "defined_duty"],
        "constraints": [
          "Dynamic simulation is currently supported only for CSTR.",
          "Dynamic CSTR does not support isothermal or outlet_temperature thermal modes."
        ]
      }
    }
  }
}
```

| Field          | Type     | Required | Description                              |
|----------------|----------|----------|------------------------------------------|
| `name`           | string   | yes      | Human-readable engine name               |
| `version`        | string   | yes      | Engine software version                  |
| `description`    | string   | no       | Short description of capabilities        |
| `capabilities`   | string[] | yes      | List of capability identifiers (see below) |
| `reactorSupport` | object   | no       | Optional structured reactor capability metadata for client-side validation |

---

## Capability Vocabulary

Each capability identifier maps to one or more optional endpoints. An engine only needs to implement the endpoints for the capabilities it declares.

| Capability         | Description                                          |
|--------------------|------------------------------------------------------|
| `compounds`        | List and search available chemical compounds         |
| `property_packages`| List available thermodynamic property packages       |
| `thermodynamic`    | Calculate thermodynamic properties (Cp, H, S, G)    |
| `transport`        | Calculate transport properties (viscosity, conductivity) |
| `surface`          | Calculate surface properties (surface tension)       |
| `flash`            | Perform flash equilibrium calculations               |
| `reactor`          | Simulate chemical reactors                           |
| `verify_compounds` | Verify compound availability and suggest alternatives |

Unrecognized capabilities in the response are ignored by consumers (forward-compatible).

---

## Optional Endpoints by Capability

### `compounds`

#### `GET /compounds`

List all available compounds.

**Response (200 OK):**
```json
[
  { "name": "Water" },
  { "name": "Ethanol" },
  { "name": "Methane" }
]
```

#### `GET /compounds/search?q={query}`

Search compounds by name substring (case-insensitive).

| Parameter | Type   | Required | Description         |
|-----------|--------|----------|---------------------|
| `q`       | string | yes      | Search query string |

**Response (200 OK):** Same format as `GET /compounds`, filtered.

---

### `property_packages`

#### `GET /property-packages`

List available thermodynamic property packages.

**Response (200 OK):**
```json
["Peng-Robinson", "SRK", "NRTL", "UNIQUAC"]
```

---

### `thermodynamic`

#### `POST /properties/thermodynamic`

Calculate thermodynamic properties for a mixture at given conditions.

**Request:**
```json
{
  "compounds": ["Water", "Ethanol"],
  "composition": [0.5, 0.5],
  "propertyPackage": "Peng-Robinson",
  "temperature": 350.0,
  "pressure": 101325.0
}
```

| Field            | Type     | Required | Description                              |
|------------------|----------|----------|------------------------------------------|
| `compounds`      | string[] | yes      | Compound names                           |
| `composition`    | float[]  | yes      | Mole fractions (must sum to 1.0)         |
| `propertyPackage` | string  | yes      | Thermodynamic model name                 |
| `temperature`    | float    | yes      | Temperature in Kelvin                    |
| `pressure`       | float    | yes      | Pressure in Pascals                      |

**Response (200 OK):**
```json
{
  "temperature": 350.0,
  "pressure": 101325.0,
  "molarVolume": 0.000028,
  "enthalpy": -285000.0,
  "entropy": -163.0,
  "gibbsEnergy": -237000.0,
  "heatCapacityCp": 75.3,
  "heatCapacityCv": 74.5,
  "molecularWeight": 27.0,
  "compressibilityFactor": 0.95,
  "phases": ["Liquid"]
}
```

All numeric fields except `temperature`, `pressure`, and `phases` are nullable.

---

### `transport`

#### `POST /properties/transport`

Calculate transport properties for a mixture.

**Request:** Same fields as `thermodynamic`.

```json
{
  "compounds": ["Water", "Ethanol"],
  "composition": [0.5, 0.5],
  "propertyPackage": "Peng-Robinson",
  "temperature": 350.0,
  "pressure": 101325.0
}
```

**Response (200 OK):**
```json
{
  "temperature": 350.0,
  "pressure": 101325.0,
  "viscosityLiquid": 0.0004,
  "viscosityVapor": 0.00001,
  "thermalConductivityLiquid": 0.6,
  "thermalConductivityVapor": 0.025,
  "thermalConductivityMixture": 0.3
}
```

All transport property fields are nullable.

---

### `surface`

#### `POST /properties/surface`

Calculate surface properties for a mixture.

**Request:** Same fields as `thermodynamic`.

**Response (200 OK):**
```json
{
  "temperature": 350.0,
  "pressure": 101325.0,
  "surfaceTension": 0.072
}
```

`surfaceTension` is nullable.

---

### `flash`

#### `POST /flash/{type}`

Perform phase equilibrium (flash) calculation. The `{type}` path parameter specifies the flash specification:

| Type  | Description                        | Required Fields                  |
|-------|------------------------------------|----------------------------------|
| `pt`  | Pressure-Temperature flash         | `temperature`, `pressure`        |
| `ph`  | Pressure-Enthalpy flash            | `pressure`, `enthalpy`           |
| `ps`  | Pressure-Entropy flash             | `pressure`, `entropy`            |
| `tvf` | Temperature-Vapor Fraction flash   | `temperature`, `vaporFraction`   |
| `pvf` | Pressure-Vapor Fraction flash      | `pressure`, `vaporFraction`      |

**Request:**
```json
{
  "compounds": ["Water", "Ethanol"],
  "composition": [0.5, 0.5],
  "propertyPackage": "Peng-Robinson",
  "temperature": 373.15,
  "pressure": 101325.0,
  "enthalpy": null,
  "entropy": null,
  "vaporFraction": null
}
```

| Field            | Type     | Required | Description                              |
|------------------|----------|----------|------------------------------------------|
| `compounds`      | string[] | yes      | Compound names                           |
| `composition`    | float[]  | yes      | Mole fractions                           |
| `propertyPackage` | string  | yes      | Thermodynamic model name                 |
| `temperature`    | float?   | depends  | Temperature in Kelvin                    |
| `pressure`       | float?   | depends  | Pressure in Pascals                      |
| `enthalpy`       | float?   | depends  | Specific enthalpy in J/mol               |
| `entropy`        | float?   | depends  | Specific entropy in J/(mol·K)            |
| `vaporFraction`  | float?   | depends  | Vapor molar fraction (0.0 to 1.0)        |

**Response (200 OK):**
```json
{
  "temperature": 373.15,
  "pressure": 101325.0,
  "vaporFraction": 0.45,
  "phases": [
    {
      "phaseName": "Vapor",
      "fraction": 0.45,
      "composition": { "Water": 0.6, "Ethanol": 0.4 },
      "temperature": 373.15,
      "pressure": 101325.0,
      "enthalpy": -240000.0,
      "entropy": -150.0,
      "molarVolume": null
    },
    {
      "phaseName": "Liquid",
      "fraction": 0.55,
      "composition": { "Water": 0.42, "Ethanol": 0.58 },
      "temperature": 373.15,
      "pressure": 101325.0,
      "enthalpy": -280000.0,
      "entropy": -165.0,
      "molarVolume": null
    }
  ],
  "errors": []
}
```

---

### `reactor`

#### `POST /reactor/simulate`

Simulate a chemical reactor with specified reactions and inlet streams.

**Request:**
```json
{
  "compounds": ["Ethanol", "Water", "Acetic Acid", "Ethyl Acetate"],
  "propertyPackage": "NRTL",
  "simulationMode": "steady_state",
  "reactorType": "PFR",
  "inletStreams": [
    {
      "temperature": 350.0,
      "pressure": 101325.0,
      "totalFlow": 100.0,
      "flowBasis": "molar",
      "composition": { "Ethanol": 0.5, "Acetic Acid": 0.5 }
    }
  ],
  "reactions": [
    {
      "name": "Esterification",
      "type": "Kinetic",
      "compounds": { "Ethanol": -1, "Acetic Acid": -1, "Ethyl Acetate": 1, "Water": 1 },
      "baseCompound": "Ethanol",
      "phase": "Liquid",
      "basis": "MolarConc",
      "aForward": 1e6,
      "eForward": 50000.0,
      "aReverse": 1e4,
      "eReverse": 60000.0
    }
  ],
  "reactorVolume": 10.0,
  "thermalMode": "isothermal",
  "pressureDrop": 0.0,
  "convergenceTolerance": 1e-6,
  "maxIterations": 100,
  "numberOfSegments": 10,
  "timeoutSeconds": 120
}
```

| Field                   | Type      | Required | Description                                |
|-------------------------|-----------|----------|--------------------------------------------|
| `compounds`             | string[]  | yes      | All compounds in the system                |
| `propertyPackage`       | string    | yes      | Thermodynamic model                        |
| `simulationMode`        | string    | no       | `"steady_state"` (default) or `"dynamic"` |
| `reactorType`           | string    | yes      | `"PFR"` or `"CSTR"`                       |
| `inletStreams`          | object[]  | yes      | One or more inlet stream definitions       |
| `reactions`             | object[]  | yes      | Reaction definitions                       |
| `transient`             | object?   | no       | Required for `simulationMode="dynamic"`    |
| `reactorVolume`         | float?    | no       | Reactor volume in m³                       |
| `reactorLength`         | float?    | no       | Reactor length in m (PFR)                  |
| `reactorDiameter`       | float?    | no       | Reactor diameter in m (PFR)                |
| `numberOfTubes`         | int       | no       | Number of tubes (default: 1)               |
| `thermalMode`           | string    | no       | `"isothermal"`, `"adiabatic"`, `"outlet_temperature"`, or `"defined_duty"` |
| `outletTemperature`     | float?    | no       | Target outlet temperature in K             |
| `heatDuty`              | float?    | no       | Heat duty in W                             |
| `pressureDrop`          | float     | no       | Pressure drop in Pa (default: 0)           |
| `convergenceTolerance`  | float     | no       | Solver tolerance (default: 1e-6)           |
| `maxIterations`         | int       | no       | Max solver iterations (default: 100)       |
| `numberOfSegments`      | int       | no       | PFR discretization segments (default: 10)  |
| `timeoutSeconds`        | int       | no       | Timeout in seconds (default: 120)          |

**Inlet Stream:**

| Field         | Type   | Required | Description                     |
|---------------|--------|----------|---------------------------------|
| `temperature` | float  | yes      | Temperature in K                |
| `pressure`    | float  | yes      | Pressure in Pa                  |
| `totalFlow`   | float  | yes      | Total flow rate                 |
| `flowBasis`   | string | no       | `"molar"` or `"mass"` (default: `"molar"`) |
| `composition` | object | no       | Compound name → mole/mass fraction map |

**Reaction:**

| Field                  | Type   | Required | Description                                |
|------------------------|--------|----------|--------------------------------------------|
| `name`                 | string | yes      | Reaction name                              |
| `type`                 | string | yes      | `"Kinetic"`, `"Conversion"`, or `"Equilibrium"` |
| `compounds`            | object | yes      | Compound → stoichiometric coefficient map  |
| `baseCompound`         | string | yes      | Reference compound for conversion          |
| `phase`                | string | no       | `"Liquid"` or `"Vapor"` (default: `"Liquid"`) |
| `basis`                | string | no       | Rate basis (default: `"MolarConc"`)        |
| `aForward`             | float  | no       | Forward pre-exponential factor             |
| `eForward`             | float  | no       | Forward activation energy in J/mol         |
| `aReverse`             | float  | no       | Reverse pre-exponential factor             |
| `eReverse`             | float  | no       | Reverse activation energy in J/mol         |
| `directOrders`         | object | no       | Forward reaction orders                    |
| `reverseOrders`        | object | no       | Reverse reaction orders                    |
| `conversionExpression` | string | no       | Expression for conversion-type reactions   |
| `keqExpression`        | string | no       | Equilibrium constant expression            |
| `approachTemperature`  | float  | no       | Approach temperature for equilibrium in K  |

**Transient Settings:**

| Field                 | Type     | Required | Description |
|-----------------------|----------|----------|-------------|
| `finalTime`           | float?   | depends  | Final simulation time in seconds. Required unless `timeGrid` is provided |
| `timeStep`            | float?   | depends  | Uniform output step in seconds. Mutually exclusive with `numberOfPoints` and `timeGrid` |
| `numberOfPoints`      | int?     | depends  | Uniform number of output points including `t=0` and `finalTime` |
| `timeGrid`            | float[]? | depends  | Explicit monotonically increasing output times in seconds |
| `initializeFromInlet` | bool     | no       | Initialize reactor holdup from inlet conditions (default: `true`) |
| `resetContents`       | bool     | no       | Reset reactor holdup before the transient run (default: `true`) |

Dynamic simulation currently supports `CSTR` only. For dynamic `CSTR`, `thermalMode` must be `adiabatic` or `defined_duty`.
For practical runtime protection, dynamic output is limited to at most `500` time points regardless of whether the grid is provided via `timeGrid`, `numberOfPoints`, or derived from `finalTime / timeStep`.
Additionally, `timeStep` must be at least `0.1` seconds.

For interoperability, the API also accepts a small set of normalized aliases for enum-like inputs. For example, `Isothermic`, `steady-state`, `outlet temperature`, and `specified duty` are normalized to their canonical values.

**Response (200 OK):**
```json
{
  "status": "Success",
  "outletStream": {
    "temperature": 350.0,
    "pressure": 101325.0,
    "totalFlow": 100.0,
    "flowBasis": "molar",
    "composition": { "Ethanol": 0.2, "Acetic Acid": 0.2, "Ethyl Acetate": 0.3, "Water": 0.3 },
    "vaporFraction": 0.0,
    "enthalpy": -280000.0,
    "entropy": -165.0
  },
  "conversions": { "Ethanol": 0.6 },
  "heatDuty": 0.0,
  "residenceTime": 100.0,
  "profiles": {
    "position": [0.0, 1.0, 2.0],
    "temperature": [350.0, 350.0, 350.0],
    "compositions": {
      "Ethanol": [0.5, 0.35, 0.2],
      "Ethyl Acetate": [0.0, 0.15, 0.3]
    }
  },
  "transientProfiles": null,
  "errors": [],
  "warnings": []
}
```

For dynamic `CSTR` runs, `profiles` remains `null` and `transientProfiles` is populated instead:

```json
{
  "status": "Success",
  "outletStream": {
    "temperature": 348.6,
    "pressure": 101325.0,
    "totalFlow": 100.0,
    "flowBasis": "molar",
    "composition": { "Ethanol": 0.41, "Acetic Acid": 0.41, "Ethyl Acetate": 0.09, "Water": 0.09 },
    "vaporFraction": 0.0,
    "enthalpy": -280000.0,
    "entropy": -165.0
  },
  "conversions": { "Ethanol": 0.18 },
  "heatDuty": 0.0,
  "residenceTime": 100.0,
  "profiles": null,
  "transientProfiles": {
    "time": [0.0, 10.0, 20.0, 30.0],
    "temperature": [350.0, 349.4, 349.0, 348.6],
    "pressure": [101325.0, 101325.0, 101325.0, 101325.0],
    "compositions": {
      "Ethanol": [0.5, 0.46, 0.43, 0.41],
      "Ethyl Acetate": [0.0, 0.04, 0.07, 0.09]
    }
  },
  "errors": [],
  "warnings": []
}
```

---

### `verify_compounds`

#### `POST /verify-compounds`

Verify that compound names exist in the engine's database and suggest alternatives for unrecognized names.

**Request:**
```json
{
  "compounds": ["Water", "H2O", "Unobtanium"]
}
```

**Response (200 OK):**
```json
[
  { "name": "Water", "status": "found", "candidates": ["Water"] },
  { "name": "H2O", "status": "ambiguous", "candidates": ["Water", "H2O2"] },
  { "name": "Unobtanium", "status": "not_found", "candidates": null }
]
```

| Status      | Meaning                                              |
|-------------|------------------------------------------------------|
| `found`     | Exact match (case-insensitive). `candidates` contains the canonical name. |
| `ambiguous` | No exact match but partial matches found. `candidates` lists up to 10 suggestions. |
| `not_found` | No matches at all. `candidates` is null.             |

---

## Error Format

All endpoints use a standard error response format:

```json
{
  "error": "Brief error description",
  "detail": "Detailed message, duplicated from error for validation failures",
  "code": "machine_readable_error_code",
  "status": 400,
  "suggestions": [
    "Optional remediation hint"
  ]
}
```

Additional notes:
- `error` remains the primary human-readable error message for backward compatibility.
- `detail` is populated for validation errors as well, so clients that historically displayed `detail` continue to work.
- `code`, `status`, and `suggestions` are optional and may be omitted by some endpoints or implementations.

HTTP status codes:
- `400` — Invalid request parameters
- `422` — Calculation failed (valid input but computation error)
- `500` — Internal server error

---

## Versioning

- The capabilities list is forward-compatible: consumers ignore unrecognized capabilities
- New capabilities can be added without breaking existing consumers
- Breaking changes to existing endpoints require a new API version
- The `version` field in `/capabilities` and `/health` tracks the engine software version, not the API version
