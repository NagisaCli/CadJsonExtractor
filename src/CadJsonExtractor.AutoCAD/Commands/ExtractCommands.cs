using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CadJsonExtractor.AutoCAD.Export;
using CadJsonExtractor.AutoCAD.Extraction;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Geometry;
using CadJsonExtractor.Core.Serialization;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(CadJsonExtractor.AutoCAD.Commands.ExtractCommands))]

namespace CadJsonExtractor.AutoCAD.Commands
{
    /// <summary>
    /// AutoCAD plugin commands for extracting CAD geometry and metadata to JSON:
    ///   EXTRACTJSON / EXPORTJSON / CAD2JSON - Export selected geometry to a .json file
    ///   COPYJSON / JSONCOPY                 - Extract selected geometry and copy JSON to clipboard
    ///   JSONCLEANTEMP                       - Clean temporary JSON cache files
    ///
    /// Also supports legacy compatibility aliases:
    ///   E3DCOPY / E3DCOPYBASE / CP2E3D / E3DEXPORTFILE / E3DCLEANTEMP
    /// </summary>
    public class ExtractCommands
    {
        private const string PluginVersion = "1.0.0";

        #region Commands: File Export

        [CommandMethod("JS", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("EXTRACTJSON", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("EXPORTJSON", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("CAD2JSON", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("CADJSON", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("E3DEXPORTFILE", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExportJsonFile()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            CadJsonPayload payload = BuildPayload(doc, ed, promptForBase: true, out bool ok);
            if (!ok || payload == null) return;

            var pso = new PromptSaveFileOptions("Export CAD Geometry to JSON")
            {
                Filter = "JSON files (*.json)|*.json|Cad2E3D payload (*.cad2e3d.json)|*.cad2e3d.json|All files (*.*)|*.*",
                DialogName = "Export CAD JSON"
            };
            PromptFileNameResult fr = ed.GetFileNameForSave(pso);
            if (fr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nExport cancelled.");
                return;
            }

            try
            {
                ClipboardBridge.ExportToFile(payload, fr.StringResult, indented: true);
                ed.WriteMessage($"\nCadJsonExtractor: Successfully exported {payload.Groups.Count} group(s), {payload.TotalPrimitives} primitive(s) to: {fr.StringResult}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nCadJsonExtractor: Export failed - " + ex.Message);
            }
        }

        #endregion

        #region Commands: Clipboard Copy

        [CommandMethod("JSC", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("JSCOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("COPYJSON", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("JSONCOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("E3DCOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("E3DCOPYBASE", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("E3DCPB", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("CP2E3D", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void CopyJsonClipboard() => RunCopy(promptForBase: true);

        #endregion

        #region Commands: Cleanup

        [CommandMethod("JSONCLEANTEMP", CommandFlags.Modal)]
        [CommandMethod("E3DCLEANTEMP", CommandFlags.Modal)]
        public void CleanTemp()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            int n = ClipboardBridge.CleanupTempFiles(TimeSpan.Zero);
            ed?.WriteMessage($"\nCadJsonExtractor: Removed {n} temporary file(s).");
        }

        #endregion

        #region Extraction & Payload Building

        private void RunCopy(bool promptForBase)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            CadJsonPayload payload = BuildPayload(doc, ed, promptForBase, out bool ok);
            if (!ok || payload == null) return;

            var result = ClipboardBridge.Publish(payload);
            if (!result.Success)
            {
                ed.WriteMessage("\nCadJsonExtractor: Clipboard publish failed - " + result.Error);
                return;
            }

            int prims = payload.TotalPrimitives;
            int unsupported = payload.Groups.Sum(g => g.Unsupported.Sum(u => u.Count));

            ed.WriteMessage($"\nCadJsonExtractor: Extracted and copied {payload.Groups.Count} CAD group(s), {prims} primitive(s), {result.JsonBytes} bytes to clipboard.");
            if (payload.BatchBasePoint != null)
            {
                ed.WriteMessage($"\n  Base point (WCS): ({payload.BatchBasePoint.X:0.###}, {payload.BatchBasePoint.Y:0.###}).");
            }
            if (unsupported > 0)
            {
                ed.WriteMessage($"\n  {unsupported} unsupported object(s) reported.");
            }
            if (result.UsedFileFallback)
            {
                ed.WriteMessage($"\n  Large payload written to temporary file: {result.FilePath}");
            }
        }

        private CadJsonPayload BuildPayload(Document doc, Editor ed, bool promptForBase, out bool ok)
        {
            ok = false;

            // PickFirst selection or prompt
            PromptSelectionResult sel = ed.SelectImplied();
            if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
            {
                ed.WriteMessage("\nSelect CAD entities to extract to JSON...");
                sel = ed.GetSelection();
            }
            else
            {
                ed.SetImpliedSelection(new ObjectId[0]);
            }

            if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
            {
                ed.WriteMessage("\nCadJsonExtractor: Nothing selected.");
                return null;
            }

            // Reference base point
            Point3d basePoint = Point3d.Origin.TransformBy(ed.CurrentUserCoordinateSystem);
            if (promptForBase)
            {
                var ppo = new PromptPointOptions("\nSpecify reference base point for JSON coordinates [press Enter for (0,0)]: ")
                {
                    AllowNone = true
                };
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status == PromptStatus.OK)
                {
                    basePoint = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                }
                else if (ppr.Status == PromptStatus.None)
                {
                    basePoint = Point3d.Origin.TransformBy(ed.CurrentUserCoordinateSystem);
                }
                else
                {
                    ed.WriteMessage("\nCancelled.");
                    return null;
                }
            }

            var opt = new ExtractOptions();
            var payload = new CadJsonPayload
            {
                SourceDrawing = doc.Name,
                SourceApplication = "AutoCAD " + SafeSysVar("ACADVER"),
                SourceUnits = DetectUnits(doc.Database),
                BatchBasePoint = new Pt2(basePoint.X, basePoint.Y),
                ExporterVersion = PluginVersion
            };

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    var ids = sel.Value.GetObjectIds();
                    var blockRefs = new List<BlockReference>();
                    var loose = new List<Entity>();

                    foreach (ObjectId id in ids)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        if (ent is BlockReference br) blockRefs.Add(br);
                        else loose.Add(ent);
                    }

                    // 1. BlockReferences
                    int seq = 1;
                    foreach (var br in blockRefs)
                    {
                        var g = BuildGroupFromBlock(br, tr, opt, ref seq);
                        if (g != null) payload.Groups.Add(g);
                    }

                    // 2. Loose entities
                    if (loose.Count > 0)
                    {
                        var g = new CadGroupDto
                        {
                            SourceId = "loose-" + seq,
                            GroupingMode = GroupingMode.ManualBatch,
                            SourceBlockName = "LOOSE",
                            SourceTag = null,
                            InsertionPoint = new Pt2(basePoint.X, basePoint.Y),
                            LocalBasePoint = new Pt2(basePoint.X, basePoint.Y)
                        };
                        foreach (var e in loose)
                        {
                            g.SourceHandles.Add(e.Handle.ToString());
                            EntityExtractor.Extract(e, Matrix3d.Identity, tr, g, opt);
                        }
                        g.Bounds = ComputeBounds(g);
                        g.SuggestedName = "/CAD-ITEM-" + seq.ToString("000", CultureInfo.InvariantCulture);
                        if (g.Primitives.Count > 0 || g.Unsupported.Count > 0)
                        {
                            payload.Groups.Add(g);
                            ed.WriteMessage($"\n  {loose.Count} loose entity(ies) extracted as group {g.SuggestedName}.");
                        }
                        seq++;
                    }

                    tr.Commit();

                    // Geometry optimization
                    int rawLinesTotal = 0;
                    int optLinesTotal = 0;
                    var normOpt = new NormalizeOptions();

                    foreach (var g in payload.Groups)
                    {
                        if (g.Primitives == null || g.Primitives.Count == 0) continue;
                        rawLinesTotal += g.Primitives.Count;
                        g.Primitives = Normalizer.Normalize(g.Primitives, normOpt, out var _);
                        optLinesTotal += g.Primitives.Count;
                        g.Bounds = ComputeBounds(g);
                    }

                    if (rawLinesTotal > optLinesTotal)
                    {
                        int reduced = rawLinesTotal - optLinesTotal;
                        double pct = (double)reduced / rawLinesTotal * 100.0;
                        ed.WriteMessage($"\n  CadJsonExtractor: Optimized geometry, fused {reduced} redundant lines ({pct:0.#}% reduction).");
                    }
                }
                catch (System.Exception ex)
                {
                    tr.Abort();
                    ed.WriteMessage("\nCadJsonExtractor: Extraction failed - " + ex.Message);
                    return null;
                }
            }

            if (payload.Groups.Count == 0)
            {
                ed.WriteMessage("\nCadJsonExtractor: No supported geometry found in selection.");
                return null;
            }

            ValidationResult vr = PayloadSerializer.Validate(payload);
            foreach (var w in vr.Warnings) ed.WriteMessage("\n  Warning: " + w);
            if (!vr.IsValid)
            {
                foreach (var e in vr.Errors) ed.WriteMessage("\n  Error: " + e);
                ed.WriteMessage("\nCadJsonExtractor: Payload validation failed.");
                return null;
            }

            ok = true;
            return payload;
        }

        private CadGroupDto BuildGroupFromBlock(BlockReference br, Transaction tr, ExtractOptions opt, ref int seq)
        {
            string effectiveName = GetEffectiveBlockName(br, tr);

            var g = new CadGroupDto
            {
                SourceId = br.Handle.ToString(),
                GroupingMode = GroupingMode.BlockReference,
                SourceBlockName = effectiveName,
                SourceTag = ReadTag(br, tr, opt.TagAttributes),
                InsertionPoint = new Pt2(br.Position.X, br.Position.Y),
                LocalBasePoint = new Pt2(br.Position.X, br.Position.Y)
            };
            g.SourceHandles.Add(br.Handle.ToString());

            EntityExtractor.Extract(br, Matrix3d.Identity, tr, g, opt);

            g.Bounds = ComputeBounds(g);
            g.SuggestedName = "/CAD-" +
                (!string.IsNullOrWhiteSpace(g.SourceTag)
                    ? g.SourceTag
                    : effectiveName + "-" + seq.ToString("000", CultureInfo.InvariantCulture));
            seq++;

            if (g.Primitives.Count == 0 && g.Unsupported.Count == 0) return null;
            return g;
        }

        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.IsDynamicBlock && !br.DynamicBlockTableRecord.IsNull)
                {
                    var dyn = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                    return dyn.Name;
                }
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            catch { return "UNKNOWN-BLOCK"; }
        }

        private static string ReadTag(BlockReference br, Transaction tr, string[] wanted)
        {
            try
            {
                if (br.AttributeCollection == null) return null;
                var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (ObjectId id in br.AttributeCollection)
                {
                    var ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                    if (ar == null) continue;
                    if (!string.IsNullOrWhiteSpace(ar.Tag) && !found.ContainsKey(ar.Tag))
                        found[ar.Tag] = ar.TextString;
                }

                foreach (string tag in wanted)
                {
                    if (found.TryGetValue(tag, out string v) && !string.IsNullOrWhiteSpace(v))
                        return v.Trim();
                }
            }
            catch { }
            return null;
        }

        private static BoundsDto ComputeBounds(CadGroupDto g)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            void Acc(Pt2 p)
            {
                if (p == null) return;
                any = true;
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            foreach (var p in g.Primitives)
            {
                switch (p.Kind)
                {
                    case PrimitiveKind.Line:
                        Acc(p.Start);
                        Acc(p.End);
                        break;
                    case PrimitiveKind.Circle:
                    case PrimitiveKind.Arc:
                        if (p.Centre != null)
                        {
                            Acc(new Pt2(p.Centre.X - p.Radius, p.Centre.Y - p.Radius));
                            Acc(new Pt2(p.Centre.X + p.Radius, p.Centre.Y + p.Radius));
                        }
                        break;
                }
            }
            if (!any) return new BoundsDto();
            return new BoundsDto { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
        }

        private static CadUnits DetectUnits(Database db)
        {
            try
            {
                switch (db.Insunits)
                {
                    case UnitsValue.Millimeters: return CadUnits.Millimetres;
                    case UnitsValue.Centimeters: return CadUnits.Centimetres;
                    case UnitsValue.Meters: return CadUnits.Metres;
                    case UnitsValue.Inches: return CadUnits.Inches;
                    case UnitsValue.Feet: return CadUnits.Feet;
                    default: return CadUnits.Millimetres;
                }
            }
            catch { return CadUnits.Millimetres; }
        }

        private static string SafeSysVar(string name)
        {
            try { return Convert.ToString(AcadApp.GetSystemVariable(name), CultureInfo.InvariantCulture); }
            catch { return "unknown"; }
        }

        #endregion
    }
}
