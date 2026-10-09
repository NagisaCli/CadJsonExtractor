# CadJsonExtractor - User Manual & Guide

CadJsonExtractor is a standalone AutoCAD 2025 plugin (.NET 8 Windows x64) designed to extract 2D CAD drawing entities, blocks, and annotations into high-precision, normalized JSON data structures.

---

## 1. Installation & Loading

### Automatic Loading (ApplicationPlugins Bundle)
The plugin is distributed as an AutoCAD bundle package (`CadJsonExtractor.bundle`):
```text
%APPDATA%\Autodesk\ApplicationPlugins\CadJsonExtractor.bundle
```
When placed in this directory, AutoCAD 2025 automatically detects and registers the commands on startup. You do **not** need to run `NETLOAD` manually; typing any registered command triggers on-demand loading.

### Manual / Developer Loading
If you wish to load the assembly directly in AutoCAD:
1. Run the `NETLOAD` command in AutoCAD.
2. Select `CadJsonExtractor.AutoCAD.dll` from your build output or bundle `Contents/` folder.

---

## 2. Command Reference

### Primary JSON Commands

| Command | Shortcut / Aliases | Target Channel | Description |
|---|---|---|---|
| **`JS`** | `EXTRACTJSON`, `EXPORTJSON`, `CAD2JSON`, `CADJSON` | File (`.json`) | **Primary shortcut**: Prompts to select geometry and specify a reference base point, then prompts for a file path to save formatted JSON. |
| **`JSC`** | `JSCOPY`, `COPYJSON`, `JSONCOPY` | Windows Clipboard | **Primary shortcut**: Extracts geometry and copies the JSON directly to the Windows clipboard. |
| `JSONCLEANTEMP` | — | Disk Cleanup | Deletes temporary cached JSON payload files from the temp directory. |

### Legacy Bridge Compatibility Commands

These commands remain available for seamless integration with downstream importers (such as AVEVA E3D aid tools):

| Command | Action | Description |
|---|---|---|
| `E3DCOPY` / `CP2E3D` | Clipboard | Extracts geometry with base point prompt and copies payload to clipboard. |
| `E3DCOPYBASE` / `E3DCPB` | Clipboard | Same as above (prompts for alignment reference point). |
| `E3DEXPORTFILE` | File (`.cad2e3d.json`) | Exports payload directly to `.cad2e3d.json` file. |
| `E3DCLEANTEMP` | Maintenance | Cleans temporary CAD aid files. |


---

## 3. Step-by-Step Usage Guide

### Scenario A: Exporting Drawing Entities to a `.json` File

1. In AutoCAD 2025, open your drawing (`.dwg`).
2. Run the command **`EXTRACTJSON`** (or `EXPORTJSON` / `CAD2JSON`).
3. **Select Objects**:
   - Pick specific entities (blocks, lines, circles, text, polylines, hatches).
   - Press `Enter` to confirm the selection.
4. **Specify Reference Base Point**:
   - AutoCAD prompts: `Specify reference base point for JSON coordinates [press Enter for (0,0)]:`.
   - Click a key reference point (such as an equipment center, corner, or grid origin) or press `Enter` to use `(0, 0)`.
5. **Save Dialog**:
   - Choose your save directory and file name (e.g., `Equipment_Plan.json`).
   - Click **Save**.
6. **Result Notification**:
   - The command line reports the total groups, primitive count, and geometry optimization stats:
     ```text
     CadJsonExtractor: Successfully exported 5 group(s), 428 primitive(s) to: D:\Exports\Equipment_Plan.json
     ```

### Scenario B: Copying JSON Directly to Clipboard

1. Select your target entities in AutoCAD (or run the command first).
2. Run the command **`COPYJSON`** (or `JSONCOPY`).
3. Specify the reference base point when prompted.
4. The JSON is now placed on your Windows clipboard.
5. You can paste it (`Ctrl+V`) into any text editor, web app, or downstream automation script.
   *(Note: For very large selections exceeding 4 MB, the plugin automatically creates a temp file and places an integrity-verified envelope on the clipboard).*

---

## 4. Supported AutoCAD Entities & Conversion

| Entity Type | Extraction Behavior |
|---|---|
| **BlockReference** (INSERT) | Recursive traversal of block hierarchy. Nested rotations, scales, and offsets are mathematically multiplied through the full `BlockTransform` matrix. Resolves dynamic block anonymous records (`*U...`) to effective block names. |
| **Attributes** (ATTRIB) | Scans for equipment/item tag attributes (`TAG`, `TAG_NO`, `EQUIP_TAG`, `NAME`, `ITEM_NO`, `位号`, `设备编号`, etc.). |
| **Line** (LINE) | Extracted directly as 2D start and end coordinates. |
| **Circle** (CIRCLE) | Center point and radius preserved under uniform scaling; flattened to lines if non-uniformly scaled. |
| **Arc** (ARC) | Center, radius, CCW start angle, and end angle preserved. |
| **LWPolyline** | Straight segments become lines; bulge arcs are calculated with exact sagitta trigonometry into true circular arcs. |
| **2D / 3D Polyline** | Vertices extracted into connected line segments. |
| **DBText & MText** | Rendered into single-stroke vector lines using a built-in lightweight font engine (no external font dependencies required). |
| **Hatch** (HATCH) | Boundary loops (polylines and curves) are extracted as clean boundary lines. |
| **Dimension** (DIMENSION) | Dimension block sub-entities (arrows, extension lines, text) are expanded and extracted. |
| **Spline / Ellipse** | Tessellated into smooth chords governed by chord-error tolerance (`MaxChordError = 1.0`). |

---

## 5. Geometry Optimization Engine

Before emitting JSON, the plugin automatically runs a geometry optimization pass:
- **Collinear Merging**: Fuses micro line segments lying on the same line into single continuous segments.
- **Deduplication**: Eliminates identical overlapping geometry.
- **Zero-Length Pruning**: Removes residual dots and zero-length vectors.
- **RDP Curve Simplification**: Simplifies dense polyline paths while maintaining visual fidelity (typically reducing segment count by 40%–80%).

---

## 6. JSON Data Schema Reference

An extracted JSON payload follows this structure:

```json
{
  "schema": "cad-json-payload",
  "schemaVersion": 1,
  "sourceDrawing": "Pump_Layout.dwg",
  "sourceApplication": "AutoCAD 2025",
  "sourceUnits": "Millimetres",
  "batchBasePoint": { "x": 12500.0, "y": 8400.0 },
  "createdUtc": "2026-10-09T04:30:00Z",
  "exporterVersion": "1.0.0",
  "groups": [
    {
      "sourceId": "2A5F",
      "groupingMode": "BlockReference",
      "sourceBlockName": "PUMP_CENTRIFUGAL",
      "sourceTag": "P-101A",
      "suggestedName": "/CAD-P-101A",
      "insertionPoint": { "x": 12500.0, "y": 8400.0 },
      "localBasePoint": { "x": 12500.0, "y": 8400.0 },
      "sourceHandles": [ "2A5F" ],
      "bounds": {
        "minX": 11800.0,
        "minY": 7900.0,
        "maxX": 13200.0,
        "maxY": 8900.0
      },
      "primitives": [
        {
          "kind": "Line",
          "start": { "x": 11800.0, "y": 7900.0 },
          "end": { "x": 13200.0, "y": 7900.0 },
          "handle": "2A60"
        },
        {
          "kind": "Circle",
          "centre": { "x": 12500.0, "y": 8400.0 },
          "radius": 450.0,
          "handle": "2A61"
        },
        {
          "kind": "Arc",
          "centre": { "x": 12500.0, "y": 8400.0 },
          "radius": 600.0,
          "startAngle": 0.0,
          "endAngle": 1.570796,
          "handle": "2A62"
        }
      ],
      "unsupported": [],
      "warnings": []
    }
  ]
}
```
