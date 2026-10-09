using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadJsonExtractor.Core.Contract;

namespace CadJsonExtractor.Core.Serialization
{
    /// <summary>Outcome of validating a payload.</summary>
    public sealed class ValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;

        public void Error(string m) { Errors.Add(m); }
        public void Warn(string m) { Warnings.Add(m); }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var e in Errors) sb.AppendLine("ERROR: " + e);
            foreach (var w in Warnings) sb.AppendLine("WARN : " + w);
            return sb.ToString();
        }
    }

    /// <summary>Limits applied to payload serialization and validation.</summary>
    public sealed class ValidationLimits
    {
        public int MaxGroups { get; set; } = 5000;
        public int MaxPrimitivesPerGroup { get; set; } = 200000;
        public int MaxTotalPrimitives { get; set; } = 2000000;
        public long MaxPayloadBytes { get; set; } = 256L * 1024 * 1024;
        public int MaxClipboardBytes { get; set; } = 4 * 1024 * 1024;
    }

    /// <summary>
    /// JSON serialization and validation for CAD JSON payloads.
    /// </summary>
    public static class PayloadSerializer
    {
        private static readonly JsonSerializerOptions WriteOptions = CreateOptions(false);
        private static readonly JsonSerializerOptions ReadOptions = CreateOptions(false);
        private const int MaxEnvelopeChars = 16 * 1024;

        private static JsonSerializerOptions CreateOptions(bool indented)
        {
            var o = new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = false,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                NumberHandling = JsonNumberHandling.Strict,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        public static string Serialize(CadJsonPayload payload, bool indented = false)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return indented ? JsonSerializer.Serialize(payload, CreateOptions(true)) : JsonSerializer.Serialize(payload, WriteOptions);
        }

        public static string SerializeEnvelope(PayloadFileEnvelope env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            return JsonSerializer.Serialize(env, WriteOptions);
        }

        public static CadJsonPayload TryDeserialize(string json, out ValidationResult result)
        {
            result = new ValidationResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Error("Payload is empty.");
                return null;
            }
            try
            {
                var p = JsonSerializer.Deserialize<CadJsonPayload>(json, ReadOptions);
                if (p == null) result.Error("Payload deserialized to null.");
                return p;
            }
            catch (JsonException ex)
            {
                result.Error("Malformed JSON: " + ex.Message);
                return null;
            }
        }

        public static CadJsonPayload TryDeserialize(byte[] utf8Json, out ValidationResult result)
        {
            result = new ValidationResult();
            if (utf8Json == null || utf8Json.Length == 0)
            {
                result.Error("Payload is empty.");
                return null;
            }

            int offset = utf8Json.Length >= 3 && utf8Json[0] == 0xEF && utf8Json[1] == 0xBB && utf8Json[2] == 0xBF ? 3 : 0;
            try
            {
                var p = JsonSerializer.Deserialize<CadJsonPayload>(
                    new ReadOnlySpan<byte>(utf8Json, offset, utf8Json.Length - offset), ReadOptions);
                if (p == null) result.Error("Payload deserialized to null.");
                return p;
            }
            catch (JsonException ex)
            {
                result.Error("Malformed JSON: " + ex.Message);
                return null;
            }
        }

        public static PayloadFileEnvelope TryDeserializeEnvelope(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxEnvelopeChars) return null;
            try
            {
                var env = JsonSerializer.Deserialize<PayloadFileEnvelope>(json, ReadOptions);
                if (env != null && env.Envelope && !string.IsNullOrWhiteSpace(env.FilePath)) return env;
                return null;
            }
            catch (JsonException) { return null; }
        }

        public static ValidationResult Validate(CadJsonPayload p, ValidationLimits limits = null)
        {
            limits = limits ?? new ValidationLimits();
            var r = new ValidationResult();

            if (p == null) { r.Error("Payload is null."); return r; }

            if (!string.Equals(p.Schema, SchemaConstants.SchemaId, StringComparison.Ordinal) &&
                !string.Equals(p.Schema, SchemaConstants.LegacySchemaId, StringComparison.Ordinal))
            {
                r.Error($"Unexpected schema id '{p.Schema}' (expected '{SchemaConstants.SchemaId}' or '{SchemaConstants.LegacySchemaId}').");
            }

            if (p.SchemaVersion != SchemaConstants.SchemaVersion)
                r.Error($"Unsupported schema version {p.SchemaVersion} (this build supports {SchemaConstants.SchemaVersion}).");

            if (p.Groups == null || p.Groups.Count == 0)
            {
                r.Error("Payload contains no groups.");
                return r;
            }

            if (p.Groups.Count > limits.MaxGroups)
                r.Error($"Group count {p.Groups.Count} exceeds limit {limits.MaxGroups}.");

            int total = 0;
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int gi = 0; gi < p.Groups.Count; gi++)
            {
                var g = p.Groups[gi];
                string where = $"group[{gi}]";
                if (g == null) { r.Error(where + " is null."); continue; }

                if (string.IsNullOrWhiteSpace(g.SourceId))
                    r.Error($"{where} has no sourceId.");
                else
                {
                    where = $"group '{g.SourceId}'";
                    if (!seenIds.Add(g.SourceId))
                        r.Error($"Duplicate sourceId '{g.SourceId}'.");
                }

                if (g.Primitives == null || g.Primitives.Count == 0)
                {
                    r.Warn($"{where} contains no supported geometry and will be skipped.");
                }
                else
                {
                    if (g.Primitives.Count > limits.MaxPrimitivesPerGroup)
                        r.Error($"{where} has {g.Primitives.Count} primitives, exceeding {limits.MaxPrimitivesPerGroup}.");
                    total += g.Primitives.Count;

                    for (int i = 0; i < g.Primitives.Count; i++)
                    {
                        var prim = g.Primitives[i];
                        if (prim == null) { r.Error($"{where} primitive[{i}] is null."); continue; }
                        ValidatePrimitive(prim, $"{where} primitive[{i}]", r);
                    }
                }

                if (g.LocalBasePoint != null && !g.LocalBasePoint.IsFinite)
                    r.Error($"{where} has a non-finite localBasePoint.");
                if (g.InsertionPoint != null && !g.InsertionPoint.IsFinite)
                    r.Error($"{where} has a non-finite insertionPoint.");
            }

            if (total > limits.MaxTotalPrimitives)
                r.Error($"Total primitive count {total} exceeds limit {limits.MaxTotalPrimitives}.");

            if (p.BatchBasePoint != null && !p.BatchBasePoint.IsFinite)
                r.Error("batchBasePoint is not finite.");

            return r;
        }

        private static void ValidatePrimitive(PrimitiveDto prim, string where, ValidationResult r)
        {
            switch (prim.Kind)
            {
                case PrimitiveKind.Line:
                    if (prim.Start == null || prim.End == null)
                    { r.Error($"{where} is a Line with a missing endpoint."); return; }
                    if (!prim.Start.IsFinite || !prim.End.IsFinite)
                        r.Error($"{where} has non-finite coordinates.");
                    break;

                case PrimitiveKind.Circle:
                case PrimitiveKind.Arc:
                    if (prim.Centre == null)
                    { r.Error($"{where} is a {prim.Kind} with no centre."); return; }
                    if (!prim.Centre.IsFinite)
                        r.Error($"{where} has a non-finite centre.");
                    if (double.IsNaN(prim.Radius) || double.IsInfinity(prim.Radius))
                        r.Error($"{where} has a non-finite radius.");
                    else if (prim.Radius <= 0.0)
                        r.Error($"{where} has a non-positive radius ({prim.Radius}).");
                    if (prim.Kind == PrimitiveKind.Arc)
                    {
                        if (double.IsNaN(prim.StartAngle) || double.IsInfinity(prim.StartAngle) ||
                            double.IsNaN(prim.EndAngle) || double.IsInfinity(prim.EndAngle))
                            r.Error($"{where} has non-finite arc angles.");
                    }
                    break;

                default:
                    r.Error($"{where} has an unrecognised kind '{prim.Kind}'.");
                    break;
            }
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static string Sha256HexOfFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static CadJsonPayload ReadFromEnvelope(
            PayloadFileEnvelope env, ValidationLimits limits, out ValidationResult result)
        {
            result = new ValidationResult();
            limits = limits ?? new ValidationLimits();

            if (env == null) { result.Error("Envelope is null."); return null; }
            if (!File.Exists(env.FilePath))
            { result.Error($"Payload file not found: {env.FilePath}"); return null; }

            var fi = new FileInfo(env.FilePath);
            if (fi.Length > limits.MaxPayloadBytes)
            { result.Error($"Payload file is {fi.Length} bytes, exceeding limit {limits.MaxPayloadBytes}."); return null; }

            if (env.FileSize > 0 && fi.Length != env.FileSize)
            { result.Error($"Payload file size {fi.Length} does not match envelope value {env.FileSize}."); return null; }

            byte[] bytes = File.ReadAllBytes(env.FilePath);
            if (env.FileSize > 0 && bytes.Length != env.FileSize)
            { result.Error($"Payload file size {bytes.Length} does not match envelope value {env.FileSize}."); return null; }

            bool verified = false;
            if (!string.IsNullOrWhiteSpace(env.Sha256))
            {
                string actual = Sha256Hex(bytes);
                if (!string.Equals(actual, env.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.Error("Payload file checksum mismatch - the file was modified or is corrupt.");
                    return null;
                }
                verified = true;
            }

            var payload = TryDeserialize(bytes, out result);
            if (!verified) result.Warn("Envelope carried no checksum; integrity could not be verified.");
            return payload;
        }
    }
}
