using System;
using System.Collections.Generic;
using CadJsonExtractor.Core.Contract;

namespace CadJsonExtractor.Core.Geometry
{
    /// <summary>
    /// Converts curved primitives into line segments using a maximum chord-error tolerance.
    /// </summary>
    public static class Tessellator
    {
        public const int MinCircleSegments = 8;
        public const int MaxArcSegments = 4096;

        public static int SegmentCount(double radius, double sweepRadians, double maxChordError)
        {
            sweepRadians = Math.Abs(sweepRadians);
            if (sweepRadians <= 0.0) return 1;
            if (radius <= 0.0) return 1;
            if (maxChordError <= 0.0) return MaxArcSegments;

            double ratio = 1.0 - (maxChordError / radius);
            int n;
            if (ratio <= -1.0)
            {
                n = 1;
            }
            else
            {
                double theta = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, ratio)));
                if (theta <= 1e-12) n = MaxArcSegments;
                else n = (int)Math.Ceiling(sweepRadians / theta);
            }

            if (n < 1) n = 1;
            int minForSweep = (int)Math.Ceiling(MinCircleSegments * (sweepRadians / (2.0 * Math.PI)));
            if (minForSweep < 1) minForSweep = 1;
            if (n < minForSweep) n = minForSweep;
            if (n > MaxArcSegments) n = MaxArcSegments;
            return n;
        }

        public static List<Pt2> ArcPoints(Pt2 centre, double radius, double startAngle, double sweep, double maxChordError)
        {
            if (centre == null) throw new ArgumentNullException(nameof(centre));
            int n = SegmentCount(radius, sweep, maxChordError);
            var pts = new List<Pt2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double a = startAngle + sweep * ((double)i / n);
                pts.Add(new Pt2(centre.X + radius * Math.Cos(a), centre.Y + radius * Math.Sin(a)));
            }
            return pts;
        }

        public static double CcwSweep(double a0, double a1)
        {
            double s = a1 - a0;
            while (s < 0) s += 2.0 * Math.PI;
            while (s > 2.0 * Math.PI) s -= 2.0 * Math.PI;
            if (s <= 1e-12) s = 2.0 * Math.PI;
            return s;
        }

        public static List<PrimitiveDto> ToLines(PrimitiveDto p, double maxChordError)
        {
            if (p == null) return new List<PrimitiveDto>();

            switch (p.Kind)
            {
                case PrimitiveKind.Line:
                    return new List<PrimitiveDto> { p };

                case PrimitiveKind.Circle:
                {
                    int n = SegmentCount(p.Radius, 2.0 * Math.PI, maxChordError);
                    var lines = new List<PrimitiveDto>(n);
                    double step = (2.0 * Math.PI) / n;
                    double cx = p.Centre.X, cy = p.Centre.Y, r = p.Radius;
                    double px = cx + r, py = cy;
                    double firstX = px, firstY = py;

                    for (int i = 1; i < n; i++)
                    {
                        double a = i * step;
                        double nx = cx + r * Math.Cos(a);
                        double ny = cy + r * Math.Sin(a);
                        lines.Add(PrimitiveDto.MakeLine(new Pt2(px, py), new Pt2(nx, ny), p.SourceHandle));
                        px = nx;
                        py = ny;
                    }
                    lines.Add(PrimitiveDto.MakeLine(new Pt2(px, py), new Pt2(firstX, firstY), p.SourceHandle));
                    return lines;
                }

                case PrimitiveKind.Arc:
                {
                    double sweep = CcwSweep(p.StartAngle, p.EndAngle);
                    int n = SegmentCount(p.Radius, sweep, maxChordError);
                    var lines = new List<PrimitiveDto>(n);
                    double cx = p.Centre.X, cy = p.Centre.Y, r = p.Radius;
                    double px = cx + r * Math.Cos(p.StartAngle);
                    double py = cy + r * Math.Sin(p.StartAngle);

                    for (int i = 1; i <= n; i++)
                    {
                        double a = p.StartAngle + sweep * ((double)i / n);
                        double nx = cx + r * Math.Cos(a);
                        double ny = cy + r * Math.Sin(a);
                        lines.Add(PrimitiveDto.MakeLine(new Pt2(px, py), new Pt2(nx, ny), p.SourceHandle));
                        px = nx;
                        py = ny;
                    }
                    return lines;
                }

                default:
                    return new List<PrimitiveDto>();
            }
        }

        public static PrimitiveDto FromBulge(Pt2 a, Pt2 b, double bulge, string handle)
        {
            if (Math.Abs(bulge) < 1e-12)
                return PrimitiveDto.MakeLine(a, b, handle);

            double dx = b.X - a.X, dy = b.Y - a.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 1e-12) return null;

            double mx = (a.X + b.X) / 2.0, my = (a.Y + b.Y) / 2.0;
            double nx = -dy / chord, ny = dx / chord;

            double half = chord / 2.0;
            double sagitta = half * bulge;
            double radius = (half * half + sagitta * sagitta) / (2.0 * Math.Abs(sagitta));

            double apexX = mx + nx * sagitta;
            double apexY = my + ny * sagitta;

            double ux = nx * Math.Sign(sagitta), uy = ny * Math.Sign(sagitta);
            var centre = new Pt2(apexX - ux * radius, apexY - uy * radius);

            double a0 = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            double a1 = Math.Atan2(b.Y - centre.Y, b.X - centre.X);
            double aApex = Math.Atan2(apexY - centre.Y, apexX - centre.X);

            double spanAB = a1 - a0;
            while (spanAB < 0) spanAB += 2.0 * Math.PI;
            double spanToApex = aApex - a0;
            while (spanToApex < 0) spanToApex += 2.0 * Math.PI;

            if (spanToApex > spanAB + 1e-12)
            {
                double t = a0; a0 = a1; a1 = t;
            }
            return PrimitiveDto.MakeArc(centre, radius, a0, a1, handle);
        }
    }
}
