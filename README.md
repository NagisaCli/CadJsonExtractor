# CadJsonExtractor

A standalone, high-performance AutoCAD 2025 (.NET 8) plugin to extract 2D CAD drawing entities and block geometry into high-precision, normalized JSON payloads.

Supports both file export (`.json`) and Windows clipboard exchange with automatic large-payload file fallback and SHA-256 integrity validation.

---

## Features

- **Entity Support**: Lines, Arcs, Circles, Lightweight Polylines (bulge arcs), 2D/3D Polylines, DBText, MText, Hatches, Dimensions, and nested Block References.
- **Dynamic Block Resolution**: Automatically retrieves the effective block name (`EffectiveName`) for dynamic block instances.
- **Tag Extraction**: Automatically scans block attribute references for item/equipment tags (`TAG`, `TAG_NO`, `EQUIP_TAG`, `NAME`, `ITEM_NO`, `位号`, `编号`, etc.).
- **Geometric Normalization**: Fuses collinear line segments, eliminates duplicate geometry, removes zero-length micro-segments, and simplifies dense curves within configurable tolerance limits.
- **Stroke Font Text**: Decomposes text strings into clean vector line strokes suitable for downstream CAD/3D platforms without bloated font dependencies.
- **Multiple Output Channels**:
  - Direct file export with file save dialog (`EXTRACTJSON` / `EXPORTJSON` / `CAD2JSON`).
  - Windows clipboard (`COPYJSON` / `JSONCOPY`) with UTF-8 JSON text and custom clipboard format.
  - Backward-compatible bridge commands (`E3DCOPY`, `E3DCOPYBASE`, `CP2E3D`, `E3DEXPORTFILE`).

---

## Command Reference

| Command | Action | Description |
|---|---|---|
| `EXTRACTJSON` / `EXPORTJSON` / `CAD2JSON` | File Export | Prompts for entity selection and base point, opens save dialog, writes formatted `.json`. |
| `COPYJSON` / `JSONCOPY` | Clipboard | Prompts for entity selection and base point, copies JSON to clipboard. |
| `JSONCLEANTEMP` | Maintenance | Cleans temporary cache payload files from the temp directory. |
| `E3DCOPY` / `CP2E3D` / `E3DCOPYBASE` | Compatibility | Legacy copy commands compatible with AVEVA E3D aid importers. |
| `E3DEXPORTFILE` | Compatibility | Legacy command to export to `.cad2e3d.json`. |

---

## Architecture & Project Structure

```text
CadJsonExtractor/
├── CadJsonExtractor.slnx               # Modern solution file
├── AGENTS.md                           # Strict language and compatibility rules
├── README.md                           # Plugin documentation
├── src/
│   ├── CadJsonExtractor.Core/          # Multi-targeted (.NET 8 & .NET Framework 4.7.2)
│   │   ├── Contract/                   # JSON schemas, DTOs, and unit definitions
│   │   ├── Geometry/                   # Tessellator, Normalizer, and StrokeFont
│   │   └── Serialization/              # System.Text.Json serializers and validators
│   └── CadJsonExtractor.AutoCAD/       # AutoCAD 2025 host add-in (.NET 8 Windows x64)
│       ├── Commands/                   # Registered AutoCAD command methods
│       ├── Extraction/                 # Entity and block traversal pipeline
│       └── Export/                     # Clipboard bridge and file export
├── tests/
│   └── CadJsonExtractor.Tests/         # xUnit unit test suite (.NET 8)
├── deploy/
│   └── CadJsonExtractor-1.0.0/         # AutoCAD ApplicationPlugins bundle package
│       └── CadJsonExtractor.bundle/
│           ├── PackageContents.xml
│           └── Contents/
└── tools/
    └── deploy-local.ps1                # Automated build and local deployment script
```

---

## Build and Deployment

### 1. Run Unit Tests
```powershell
dotnet test CadJsonExtractor.slnx
```

### 2. Local Deployment to AutoCAD 2025
Execute the deployment script in PowerShell:
```powershell
powershell -ExecutionPolicy Bypass -File .\tools\deploy-local.ps1
```
The script will:
1. Build `CadJsonExtractor.AutoCAD` in Release mode.
2. Stage the output DLLs into `deploy\CadJsonExtractor-1.0.0\CadJsonExtractor.bundle\Contents`.
3. Deploy the bundle into `%APPDATA%\Autodesk\ApplicationPlugins\CadJsonExtractor.bundle`.
4. Safely replace any in-use locked DLLs and unblock all assemblies.

Once deployed, AutoCAD 2025 will automatically load the plugin upon typing any registered command (e.g., `EXTRACTJSON`).

---

## Documentation

For comprehensive usage instructions, step-by-step walkthroughs, and schema explanations, see the [User Manual & Guide](docs/USER_GUIDE.md).

