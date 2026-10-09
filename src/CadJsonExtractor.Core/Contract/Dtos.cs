using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CadJsonExtractor.Core.Contract
{
    /// <summary>
    /// Host-independent CAD JSON interchange contract.
    /// </summary>
    public static class SchemaConstants
    {
        public const string SchemaId = "cad-json-payload";
        public const string LegacySchemaId = "cad2e3d-aid";
        public const int SchemaVersion = 1;

        /// <summary>Windows clipboard custom format name.</summary>
        public const string ClipboardFormat = "CadJsonExtractor.Json.v1";
        public const string LegacyClipboardFormat = "Cad2E3DAids.Json.v1";

        /// <summary>Standard JSON extension and legacy extension.</summary>
        public const string FileExtension = ".json";
        public const string LegacyFileExtension = ".cad2e3d.json";
    }

    /// <summary>Units of the source CAD drawing.</summary>
    public enum CadUnits
    {
        Unknown = 0,
        Millimetres = 1,
        Centimetres = 2,
        Metres = 3,
        Inches = 4,
        Feet = 5,
        Raw1to1 = 6
    }

    /// <summary>Kind of a supported geometry primitive in the neutral payload.</summary>
    public enum PrimitiveKind
    {
        Line = 0,
        Arc = 1,
        Circle = 2
    }

    /// <summary>How grouping was determined on the AutoCAD side.</summary>
    public enum GroupingMode
    {
        BlockReference = 0,
        NamedGroup = 1,
        ManualBatch = 2,
        SpatialCluster = 3
    }

    /// <summary>A 2D point in the source drawing coordinate system.</summary>
    public sealed class Pt2
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }

        public Pt2() { }
        public Pt2(double x, double y) { X = x; Y = y; }

        [JsonIgnore]
        public bool IsFinite =>
            !double.IsNaN(X) && !double.IsInfinity(X) &&
            !double.IsNaN(Y) && !double.IsInfinity(Y);

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###},{1:0.###})", X, Y);
    }

    /// <summary>
    /// One geometry primitive, flattened to the source 2D plane and resolved through block transforms.
    /// </summary>
    public sealed class PrimitiveDto
    {
        [JsonPropertyName("kind")] public PrimitiveKind Kind { get; set; }

        /// <summary>Line: start point. Arc/Circle: unused (null).</summary>
        [JsonPropertyName("start")] public Pt2 Start { get; set; }
        /// <summary>Line: end point. Arc/Circle: unused (null).</summary>
        [JsonPropertyName("end")] public Pt2 End { get; set; }

        /// <summary>Arc/Circle: centre point.</summary>
        [JsonPropertyName("centre")] public Pt2 Centre { get; set; }
        /// <summary>Arc/Circle: radius in source units.</summary>
        [JsonPropertyName("radius")] public double Radius { get; set; }
        /// <summary>Arc only: start angle, radians, CCW from +X.</summary>
        [JsonPropertyName("startAngle")] public double StartAngle { get; set; }
        /// <summary>Arc only: end angle, radians, CCW from +X.</summary>
        [JsonPropertyName("endAngle")] public double EndAngle { get; set; }

        /// <summary>Source AutoCAD handle, for traceability.</summary>
        [JsonPropertyName("handle")] public string SourceHandle { get; set; }

        public static PrimitiveDto MakeLine(Pt2 a, Pt2 b, string handle = null) =>
            new PrimitiveDto { Kind = PrimitiveKind.Line, Start = a, End = b, SourceHandle = handle };

        public static PrimitiveDto MakeCircle(Pt2 c, double r, string handle = null) =>
            new PrimitiveDto { Kind = PrimitiveKind.Circle, Centre = c, Radius = r, SourceHandle = handle };

        public static PrimitiveDto MakeArc(Pt2 c, double r, double a0, double a1, string handle = null) =>
            new PrimitiveDto { Kind = PrimitiveKind.Arc, Centre = c, Radius = r, StartAngle = a0, EndAngle = a1, SourceHandle = handle };
    }

    /// <summary>An entity that could not be converted, reported rather than dropped.</summary>
    public sealed class UnsupportedEntityDto
    {
        [JsonPropertyName("dxfName")] public string DxfName { get; set; }
        [JsonPropertyName("handle")] public string SourceHandle { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; } = 1;
    }

    /// <summary>Axis-aligned bounding box of a group in source units.</summary>
    public sealed class BoundsDto
    {
        [JsonPropertyName("minX")] public double MinX { get; set; }
        [JsonPropertyName("minY")] public double MinY { get; set; }
        [JsonPropertyName("maxX")] public double MaxX { get; set; }
        [JsonPropertyName("maxY")] public double MaxY { get; set; }
    }

    /// <summary>One component / block / selection group.</summary>
    public class CadGroupDto
    {
        [JsonPropertyName("sourceId")] public string SourceId { get; set; }
        [JsonPropertyName("groupingMode")] public GroupingMode GroupingMode { get; set; }
        /// <summary>Effective block name (dynamic blocks resolve to the effective name).</summary>
        [JsonPropertyName("sourceBlockName")] public string SourceBlockName { get; set; }
        /// <summary>Tag read from a configured block attribute, may be null.</summary>
        [JsonPropertyName("sourceTag")] public string SourceTag { get; set; }
        /// <summary>Suggested name.</summary>
        [JsonPropertyName("suggestedName")] public string SuggestedName { get; set; }
        /// <summary>Target name override.</summary>
        [JsonIgnore] public string TargetNameOverride { get; set; }
        /// <summary>Block insertion point in drawing coordinates.</summary>
        [JsonPropertyName("insertionPoint")] public Pt2 InsertionPoint { get; set; }
        /// <summary>Per-group local base point used by coordinate transformation.</summary>
        [JsonPropertyName("localBasePoint")] public Pt2 LocalBasePoint { get; set; }
        [JsonPropertyName("sourceHandles")] public List<string> SourceHandles { get; set; } = new List<string>();
        [JsonPropertyName("bounds")] public BoundsDto Bounds { get; set; }
        [JsonPropertyName("primitives")] public List<PrimitiveDto> Primitives { get; set; } = new List<PrimitiveDto>();
        [JsonPropertyName("unsupported")] public List<UnsupportedEntityDto> Unsupported { get; set; } = new List<UnsupportedEntityDto>();
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Backward-compatible alias for PumpGroupDto.</summary>
    public sealed class PumpGroupDto : CadGroupDto { }

    /// <summary>Root of the neutral payload.</summary>
    public class CadJsonPayload
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = SchemaConstants.SchemaId;
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = SchemaConstants.SchemaVersion;
        [JsonPropertyName("sourceDrawing")] public string SourceDrawing { get; set; }
        [JsonPropertyName("sourceApplication")] public string SourceApplication { get; set; }
        [JsonPropertyName("sourceUnits")] public CadUnits SourceUnits { get; set; } = CadUnits.Millimetres;
        /// <summary>Batch base point picked by the user, in drawing coordinates.</summary>
        [JsonPropertyName("batchBasePoint")] public Pt2 BatchBasePoint { get; set; }
        [JsonPropertyName("createdUtc")] public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("exporterVersion")] public string ExporterVersion { get; set; }
        [JsonPropertyName("groups")] public List<CadGroupDto> Groups { get; set; } = new List<CadGroupDto>();

        [JsonIgnore]
        public int TotalPrimitives
        {
            get
            {
                int n = 0;
                if (Groups != null)
                    foreach (var g in Groups) n += g.Primitives == null ? 0 : g.Primitives.Count;
                return n;
            }
        }
    }

    /// <summary>Backward-compatible alias for Cad2E3DPayload.</summary>
    public sealed class Cad2E3DPayload : CadJsonPayload { }

    /// <summary>
    /// Small clipboard envelope used when the payload exceeds the clipboard size limit.
    /// </summary>
    public sealed class PayloadFileEnvelope
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = SchemaConstants.SchemaId;
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = SchemaConstants.SchemaVersion;
        [JsonPropertyName("envelope")] public bool Envelope { get; set; } = true;
        [JsonPropertyName("filePath")] public string FilePath { get; set; }
        [JsonPropertyName("fileSize")] public long FileSize { get; set; }
        /// <summary>Lowercase hex SHA-256 of the file bytes.</summary>
        [JsonPropertyName("sha256")] public string Sha256 { get; set; }
    }
}
