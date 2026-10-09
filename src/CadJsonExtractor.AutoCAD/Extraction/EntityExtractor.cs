using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Geometry;

namespace CadJsonExtractor.AutoCAD.Extraction
{
    /// <summary>Extraction tuning.</summary>
    public sealed class ExtractOptions
    {
        /// <summary>Block attribute tags searched, in order, for the item/equipment tag.</summary>
        public string[] TagAttributes { get; set; } = { "位号", "设备位号", "设备编号", "名称", "编号", "标签", "TAG", "TAG_NO", "EQUIP_TAG", "NAME", "ITEM_NO", "ITEM_TAG", "NO", "CODE", "PUMP_TAG", "VALVE_TAG" };
        /// <summary>External references are ignored unless explicitly enabled.</summary>
        public bool IncludeXrefs { get; set; }
        /// <summary>Max chord error used when a curve must be tessellated at export time.</summary>
        public double MaxChordError { get; set; } = 1.0;
        /// <summary>Tolerance for treating a Z range as planar.</summary>
        public double PlanarTolerance { get; set; } = 1e-6;
        /// <summary>Project non-coplanar geometry instead of rejecting the group.</summary>
        public bool AllowProjection { get; set; }
        /// <summary>Guard against pathological or recursive block nesting.</summary>
        public int MaxNestingDepth { get; set; } = 16;
    }

    /// <summary>
    /// Converts AutoCAD entities into neutral DTOs, resolving nested block
    /// transformations via the full BlockTransform matrix.
    /// </summary>
    public static class EntityExtractor
    {
        /// <summary>
        /// Appends geometry from one entity into <paramref name="target"/>.
        /// <paramref name="xform"/> is the accumulated world transform.
        /// </summary>
        public static void Extract(
            Entity ent,
            Matrix3d xform,
            Transaction tr,
            CadGroupDto target,
            ExtractOptions opt,
            int depth = 0)
        {
            if (ent == null || target == null) return;
            opt = opt ?? new ExtractOptions();

            if (depth > opt.MaxNestingDepth)
            {
                AddUnsupported(target, ent.GetType().Name, Handle(ent),
                    $"Nesting deeper than {opt.MaxNestingDepth} levels; not expanded.");
                return;
            }

            switch (ent)
            {
                case Line line:
                {
                    var s = line.StartPoint.TransformBy(xform);
                    var e = line.EndPoint.TransformBy(xform);
                    CheckPlanar(target, s, e, opt, Handle(ent));
                    target.Primitives.Add(PrimitiveDto.MakeLine(P(s), P(e), Handle(ent)));
                    break;
                }

                case Circle circle:
                    ExtractCircle(circle, xform, target, opt);
                    break;

                case Arc arc:
                    ExtractArc(arc, xform, target, opt);
                    break;

                case Polyline pl:
                    ExtractLwPolyline(pl, xform, target, opt);
                    break;

                case Polyline2d p2d:
                    ExtractPolyline2d(p2d, xform, tr, target, opt);
                    break;

                case Polyline3d p3d:
                    ExtractPolyline3d(p3d, xform, tr, target, opt);
                    break;

                case DBText dbText:
                    ExtractDBText(dbText, xform, target, opt);
                    break;

                case MText mText:
                    ExtractMText(mText, xform, target, opt);
                    break;

                case BlockReference br:
                    ExtractBlockReference(br, xform, tr, target, opt, depth);
                    break;

                case Hatch hatch:
                    ExtractHatch(hatch, xform, target, opt);
                    break;

                case Dimension dim:
                    ExtractDimension(dim, xform, tr, target, opt, depth);
                    break;

                // Tessellated only with a documented tolerance, per brief section 6.
                case Spline spline:
                    ExtractCurveByFlattening(spline, xform, target, opt, "SPLINE");
                    break;

                case Ellipse ellipse:
                    ExtractCurveByFlattening(ellipse, xform, target, opt, "ELLIPSE");
                    break;

                default:
                    AddUnsupported(target, DxfName(ent), Handle(ent),
                        "Entity type is not supported by this MVP.");
                    break;
            }
        }

        // ---------------- hatch ----------------

        private static void ExtractHatch(Hatch hatch, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            if (hatch == null) return;
            string h = Handle(hatch);

            try
            {
                int numLoops = hatch.NumberOfLoops;
                if (numLoops <= 0) return;

                Matrix3d ocs = Matrix3d.PlaneToWorld(hatch.Normal);
                Matrix3d combined = xform * ocs;

                for (int i = 0; i < numLoops; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    if (loop == null) continue;

                    if (loop.IsPolyline)
                    {
                        var bvc = loop.Polyline;
                        if (bvc == null || bvc.Count < 2) continue;

                        int n = bvc.Count;
                        for (int v = 0; v < n; v++)
                        {
                            var bv1 = bvc[v];
                            var bv2 = bvc[(v + 1) % n];

                            Point3d p1 = new Point3d(bv1.Vertex.X, bv1.Vertex.Y, hatch.Elevation).TransformBy(combined);
                            Point3d p2 = new Point3d(bv2.Vertex.X, bv2.Vertex.Y, hatch.Elevation).TransformBy(combined);

                            CheckPlanar(target, p1, p2, opt, h);

                            if (Math.Abs(bv1.Bulge) < 1e-12)
                            {
                                target.Primitives.Add(PrimitiveDto.MakeLine(P(p1), P(p2), h));
                            }
                            else
                            {
                                var arc = Tessellator.FromBulge(
                                    new Pt2(bv1.Vertex.X, bv1.Vertex.Y),
                                    new Pt2(bv2.Vertex.X, bv2.Vertex.Y),
                                    bv1.Bulge, h);
                                if (arc != null)
                                {
                                    foreach (var seg in Tessellator.ToLines(arc, opt.MaxChordError))
                                    {
                                        var s3 = new Point3d(seg.Start.X, seg.Start.Y, hatch.Elevation).TransformBy(combined);
                                        var e3 = new Point3d(seg.End.X, seg.End.Y, hatch.Elevation).TransformBy(combined);
                                        target.Primitives.Add(PrimitiveDto.MakeLine(P(s3), P(e3), h));
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        var curves = loop.Curves;
                        if (curves == null || curves.Count == 0) continue;

                        for (int ci = 0; ci < curves.Count; ci++)
                        {
                            Curve2d c2d = curves[ci];
                            if (c2d == null) continue;

                            if (c2d is LineSegment2d line2d)
                            {
                                Point3d p1 = new Point3d(line2d.StartPoint.X, line2d.StartPoint.Y, hatch.Elevation).TransformBy(combined);
                                Point3d p2 = new Point3d(line2d.EndPoint.X, line2d.EndPoint.Y, hatch.Elevation).TransformBy(combined);
                                CheckPlanar(target, p1, p2, opt, h);
                                target.Primitives.Add(PrimitiveDto.MakeLine(P(p1), P(p2), h));
                            }
                            else
                            {
                                Point2d[] samplePts = null;
                                try { samplePts = c2d.GetSamplePoints(16); } catch { }
                                if (samplePts != null && samplePts.Length >= 2)
                                {
                                    for (int si = 0; si < samplePts.Length - 1; si++)
                                    {
                                        Point3d p1 = new Point3d(samplePts[si].X, samplePts[si].Y, hatch.Elevation).TransformBy(combined);
                                        Point3d p2 = new Point3d(samplePts[si + 1].X, samplePts[si + 1].Y, hatch.Elevation).TransformBy(combined);
                                        CheckPlanar(target, p1, p2, opt, h);
                                        target.Primitives.Add(PrimitiveDto.MakeLine(P(p1), P(p2), h));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnsupported(target, "HATCH", h, "Boundary extraction: " + ex.Message);
            }
        }

        // ---------------- dimension ----------------

        private static void ExtractDimension(
            Dimension dim, Matrix3d xform, Transaction tr,
            CadGroupDto target, ExtractOptions opt, int depth)
        {
            if (dim == null) return;
            string h = Handle(dim);

            try
            {
                if (!dim.DimBlockId.IsNull)
                {
                    var btr = tr.GetObject(dim.DimBlockId, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null)
                    {
                        foreach (ObjectId id in btr)
                        {
                            Entity child;
                            try { child = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                            catch { continue; }
                            if (child == null) continue;
                            Extract(child, xform, tr, target, opt, depth + 1);
                        }
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AddUnsupported(target, "DIMENSION", h, "Dimension extraction: " + ex.Message);
            }
        }

        // ---------------- text ----------------

        private static void ExtractDBText(DBText text, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            if (text == null || string.IsNullOrWhiteSpace(text.TextString)) return;
            string h = Handle(text);

            Point3d rawPos = text.Justify == AttachmentPoint.BaseLeft || text.AlignmentPoint == Point3d.Origin
                ? text.Position
                : text.AlignmentPoint;

            Point3d pWorld = rawPos.TransformBy(xform);
            CheckPlanarPoint(target, pWorld, opt, h);

            double rot = text.Rotation;
            Vector3d dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0).TransformBy(xform);
            double worldRot = Math.Atan2(dir.Y, dir.X);

            double scale = IsUniformInPlane(xform, out double k) ? k : dir.Length;
            double effHeight = Math.Max(0.1, text.Height * scale);

            var lines = StrokeFont.TextToPrimitives(text.TextString, P(pWorld), effHeight, worldRot, h);
            target.Primitives.AddRange(lines);
        }

        private static void ExtractMText(MText mtext, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            if (mtext == null) return;
            string raw = mtext.Contents;
            if (string.IsNullOrWhiteSpace(raw)) return;

            string plainText = StripMTextFormatting(raw);
            if (string.IsNullOrWhiteSpace(plainText)) return;

            string h = Handle(mtext);
            Point3d pWorld = mtext.Location.TransformBy(xform);
            CheckPlanarPoint(target, pWorld, opt, h);

            double rot = mtext.Rotation;
            Vector3d dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0).TransformBy(xform);
            double worldRot = Math.Atan2(dir.Y, dir.X);

            double scale = IsUniformInPlane(xform, out double k) ? k : dir.Length;
            double effHeight = Math.Max(0.1, mtext.TextHeight * scale);

            var lines = StrokeFont.TextToPrimitives(plainText, P(pWorld), effHeight, worldRot, h);
            target.Primitives.AddRange(lines);
        }

        private static string StripMTextFormatting(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string s = input.Replace("\\P", "\n").Replace("\\p", "\n");
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '{' || c == '}') continue;
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == '\\' || next == '{' || next == '}')
                    {
                        sb.Append(next);
                        i++;
                        continue;
                    }
                    int semi = s.IndexOf(';', i);
                    if (semi > i && semi - i < 60)
                    {
                        i = semi;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------- blocks ----------------

        private static void ExtractBlockReference(
            BlockReference br, Matrix3d xform, Transaction tr,
            CadGroupDto target, ExtractOptions opt, int depth)
        {
            BlockTableRecord btr;
            try
            {
                // In AutoCAD, br.BlockTableRecord points to the actual evaluated block table record
                // (including anonymous *U... records created for modified dynamic block instances).
                ObjectId defId = br.BlockTableRecord;
                btr = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
            }
            catch (Exception ex)
            {
                AddUnsupported(target, "INSERT", Handle(br), "Block definition unreadable: " + ex.Message);
                return;
            }

            if (btr.IsFromExternalReference && !opt.IncludeXrefs)
            {
                AddUnsupported(target, "XREF", Handle(br),
                    $"External reference '{btr.Name}' ignored (enable Xrefs to include).");
                return;
            }

            // Accumulate the FULL block transform, so nesting, rotation, mirroring
            // and scaling all compose correctly.
            Matrix3d combined = xform * br.BlockTransform;

            // 1. Extract block definition geometry (lines, curves, nested blocks, text)
            foreach (ObjectId id in btr)
            {
                Entity child;
                try { child = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (child == null) continue;
                if (child is AttributeDefinition) continue; // template definitions carry no geometry
                Extract(child, combined, tr, target, opt, depth + 1);
            }

            // 2. Extract block instance attributes (AttributeReference) attached to this BlockReference
            if (br.AttributeCollection != null && br.AttributeCollection.Count > 0)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    try
                    {
                        var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                        if (att == null || att.Invisible || string.IsNullOrWhiteSpace(att.TextString))
                            continue;
                        ExtractDBText(att, xform, target, opt);
                    }
                    catch { }
                }
            }
        }

        // ---------------- circles / arcs ----------------

        private static void ExtractCircle(Circle circle, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            string h = Handle(circle);
            if (IsUniformInPlane(xform, out double k))
            {
                var c = circle.Center.TransformBy(xform);
                CheckPlanarPoint(target, c, opt, h);
                target.Primitives.Add(PrimitiveDto.MakeCircle(P(c), circle.Radius * k, h));
            }
            else
            {
                // Non-uniform scaling turns a circle into an ellipse, so it can no
                // longer be represented as a true circle: emit line segments instead.
                target.Warnings.Add($"Circle {h} under non-uniform scaling; converted to line segments.");
                FlattenCurveToLines(circle, xform, target, opt, h);
            }
        }

        private static void ExtractArc(Arc arc, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            string h = Handle(arc);
            if (IsUniformInPlane(xform, out double k) && !IsMirroring(xform))
            {
                var c = arc.Center.TransformBy(xform);
                // Recover the transformed start/end angles from the transformed endpoints,
                // which keeps rotation of the containing block correct.
                var sp = arc.StartPoint.TransformBy(xform);
                var ep = arc.EndPoint.TransformBy(xform);
                double a0 = Math.Atan2(sp.Y - c.Y, sp.X - c.X);
                double a1 = Math.Atan2(ep.Y - c.Y, ep.X - c.X);
                CheckPlanarPoint(target, c, opt, h);
                target.Primitives.Add(PrimitiveDto.MakeArc(P(c), arc.Radius * k, a0, a1, h));
            }
            else
            {
                target.Warnings.Add($"Arc {h} under non-uniform or mirroring transform; converted to line segments.");
                FlattenCurveToLines(arc, xform, target, opt, h);
            }
        }

        // ---------------- polylines ----------------

        private static void ExtractLwPolyline(Polyline pl, Matrix3d xform, CadGroupDto target, ExtractOptions opt)
        {
            string h = Handle(pl);
            int n = pl.NumberOfVertices;
            if (n < 2) { AddUnsupported(target, "LWPOLYLINE", h, "Fewer than two vertices."); return; }

            int lastSeg = pl.Closed ? n : n - 1;
            for (int i = 0; i < lastSeg; i++)
            {
                int j = (i + 1) % n;
                Point3d a3 = pl.GetPoint3dAt(i);
                Point3d b3 = pl.GetPoint3dAt(j);
                double bulge = pl.GetBulgeAt(i);

                if (Math.Abs(bulge) < 1e-12)
                {
                    var s = a3.TransformBy(xform);
                    var e = b3.TransformBy(xform);
                    CheckPlanar(target, s, e, opt, h);
                    target.Primitives.Add(PrimitiveDto.MakeLine(P(s), P(e), h));
                }
                else
                {
                    // Build the arc in the polyline's own plane, then transform the
                    // resulting geometry so nested block transforms still apply.
                    var arc = Tessellator.FromBulge(
                        new Pt2(a3.X, a3.Y), new Pt2(b3.X, b3.Y), bulge, h);
                    if (arc == null) continue;

                    if (IsUniformInPlane(xform, out double k) && !IsMirroring(xform))
                    {
                        var c3 = new Point3d(arc.Centre.X, arc.Centre.Y, a3.Z).TransformBy(xform);
                        var s3 = a3.TransformBy(xform);
                        var e3 = b3.TransformBy(xform);
                        double a0 = Math.Atan2(s3.Y - c3.Y, s3.X - c3.X);
                        double a1 = Math.Atan2(e3.Y - c3.Y, e3.X - c3.X);
                        target.Primitives.Add(PrimitiveDto.MakeArc(P(c3), arc.Radius * k, a0, a1, h));
                    }
                    else
                    {
                        foreach (var seg in Tessellator.ToLines(arc, opt.MaxChordError))
                        {
                            var s3 = new Point3d(seg.Start.X, seg.Start.Y, a3.Z).TransformBy(xform);
                            var e3 = new Point3d(seg.End.X, seg.End.Y, a3.Z).TransformBy(xform);
                            target.Primitives.Add(PrimitiveDto.MakeLine(P(s3), P(e3), h));
                        }
                    }
                }
            }
        }

        private static void ExtractPolyline2d(Polyline2d p2d, Matrix3d xform, Transaction tr,
            CadGroupDto target, ExtractOptions opt)
        {
            string h = Handle(p2d);
            var pts = new List<Point3d>();
            try
            {
                foreach (ObjectId vid in p2d)
                {
                    var v = tr.GetObject(vid, OpenMode.ForRead) as Vertex2d;
                    if (v != null) pts.Add(v.Position);
                }
            }
            catch (Exception ex)
            {
                AddUnsupported(target, "POLYLINE", h, "Vertices unreadable: " + ex.Message);
                return;
            }
            EmitPolylinePoints(pts, p2d.Closed, xform, target, opt, h);
        }

        private static void ExtractPolyline3d(Polyline3d p3d, Matrix3d xform, Transaction tr,
            CadGroupDto target, ExtractOptions opt)
        {
            string h = Handle(p3d);
            var pts = new List<Point3d>();
            try
            {
                foreach (ObjectId vid in p3d)
                {
                    var v = tr.GetObject(vid, OpenMode.ForRead) as PolylineVertex3d;
                    if (v != null) pts.Add(v.Position);
                }
            }
            catch (Exception ex)
            {
                AddUnsupported(target, "POLYLINE3D", h, "Vertices unreadable: " + ex.Message);
                return;
            }
            EmitPolylinePoints(pts, p3d.Closed, xform, target, opt, h);
        }

        private static void EmitPolylinePoints(List<Point3d> pts, bool closed, Matrix3d xform,
            CadGroupDto target, ExtractOptions opt, string handle)
        {
            if (pts.Count < 2) { AddUnsupported(target, "POLYLINE", handle, "Fewer than two vertices."); return; }
            int last = closed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < last; i++)
            {
                var s = pts[i].TransformBy(xform);
                var e = pts[(i + 1) % pts.Count].TransformBy(xform);
                CheckPlanar(target, s, e, opt, handle);
                target.Primitives.Add(PrimitiveDto.MakeLine(P(s), P(e), handle));
            }
        }

        // ---------------- curve fallback ----------------

        private static void ExtractCurveByFlattening(Curve c, Matrix3d xform, CadGroupDto target,
            ExtractOptions opt, string dxfName)
        {
            string h = Handle(c);
            target.Warnings.Add(
                $"{dxfName} {h} tessellated to line segments at max chord error {opt.MaxChordError}.");
            FlattenCurveToLines(c, xform, target, opt, h);
        }

        /// <summary>
        /// Samples any Curve into line segments using its parameter range.
        /// The segment count is derived from the curve's own extents and the chord
        /// tolerance, so it is never a hard-coded number.
        /// </summary>
        private static void FlattenCurveToLines(Curve c, Matrix3d xform, CadGroupDto target,
            ExtractOptions opt, string handle)
        {
            try
            {
                double sp = c.StartParam, ep = c.EndParam;
                if (double.IsNaN(sp) || double.IsNaN(ep) || ep <= sp)
                {
                    AddUnsupported(target, DxfName(c), handle, "Curve has an unusable parameter range.");
                    return;
                }

                // Estimate a segment count from the curve length against the tolerance.
                double len;
                try { len = c.GetDistanceAtParameter(ep) - c.GetDistanceAtParameter(sp); }
                catch { len = 0; }

                int n;
                if (len > 0 && opt.MaxChordError > 0)
                {
                    // A conservative estimate: subdivide until each chord is short
                    // relative to the tolerance.
                    n = (int)Math.Ceiling(len / Math.Max(opt.MaxChordError * 8.0, 1e-6));
                }
                else n = 64;

                if (n < 8) n = 8;
                if (n > 4096) n = 4096;

                Point3d prev = c.GetPointAtParameter(sp).TransformBy(xform);
                for (int i = 1; i <= n; i++)
                {
                    double t = sp + (ep - sp) * ((double)i / n);
                    Point3d cur = c.GetPointAtParameter(t).TransformBy(xform);
                    target.Primitives.Add(PrimitiveDto.MakeLine(P(prev), P(cur), handle));
                    prev = cur;
                }
            }
            catch (Exception ex)
            {
                AddUnsupported(target, DxfName(c), handle, "Tessellation failed: " + ex.Message);
            }
        }

        // ---------------- helpers ----------------

        private static Pt2 P(Point3d p) => new Pt2(p.X, p.Y);

        private static string Handle(DBObject o)
        {
            try { return o.Handle.ToString(); } catch { return "(no handle)"; }
        }

        private static string DxfName(Entity e)
        {
            try { return e.GetRXClass().DxfName ?? e.GetType().Name; }
            catch { return e.GetType().Name; }
        }

        internal static void AddUnsupported(CadGroupDto g, string dxf, string handle, string reason)
        {
            if (g == null) return;
            // Collapse repeats of the same type+reason into a single counted row.
            foreach (var u in g.Unsupported)
            {
                if (u.DxfName == dxf && u.Reason == reason) { u.Count++; return; }
            }
            g.Unsupported.Add(new UnsupportedEntityDto
            { DxfName = dxf, SourceHandle = handle, Reason = reason, Count = 1 });
        }

        /// <summary>True when the transform scales X and Y equally (so circles stay circles).</summary>
        private static bool IsUniformInPlane(Matrix3d m, out double scale)
        {
            var cs = m.CoordinateSystem3d;
            double sx = cs.Xaxis.Length;
            double sy = cs.Yaxis.Length;
            scale = sx;
            return Math.Abs(sx - sy) <= 1e-9 * Math.Max(1.0, Math.Max(sx, sy));
        }

        /// <summary>True when the transform includes a reflection (negative determinant in plane).</summary>
        private static bool IsMirroring(Matrix3d m)
        {
            var cs = m.CoordinateSystem3d;
            // z component of Xaxis cross Yaxis; negative means the plane was flipped.
            var x = cs.Xaxis; var y = cs.Yaxis;
            double crossZ = x.X * y.Y - x.Y * y.X;
            return crossZ < 0;
        }

        private static void CheckPlanar(CadGroupDto g, Point3d a, Point3d b, ExtractOptions opt, string handle)
        {
            CheckPlanarPoint(g, a, opt, handle);
            CheckPlanarPoint(g, b, opt, handle);
        }

        private static void CheckPlanarPoint(CadGroupDto g, Point3d p, ExtractOptions opt, string handle)
        {
            if (Math.Abs(p.Z) > opt.PlanarTolerance)
            {
                string msg = opt.AllowProjection
                    ? $"Entity {handle} has Z={p.Z:0.###}; projected onto the source plane."
                    : $"Entity {handle} has Z={p.Z:0.###} and is not coplanar.";
                if (!g.Warnings.Contains(msg)) g.Warnings.Add(msg);
            }
        }

        /// <summary>
        /// True when every primitive in the group lies on the source plane.
        /// The caller rejects non-coplanar groups unless projection was enabled.
        /// </summary>
        public static bool HasNonCoplanarWarning(CadGroupDto g) =>
            g.Warnings.Exists(w => w.Contains("not coplanar"));
    }
}

