using System;
using System.Collections.Generic;
using CadJsonExtractor.Core.Contract;

namespace CadJsonExtractor.Core.Geometry
{
    /// <summary>
    /// Lightweight, high-performance single-stroke vector font renderer.
    /// Converts text strings into minimal, visually clean line segments suitable for
    /// AVEVA E3D 3D construction aids without blowing up primitive counts.
    /// </summary>
    public static class StrokeFont
    {
        private const double DefaultCharWidth = 0.6;
        private const double DefaultCharAdvance = 0.75;
        private const double DefaultLineSpacing = 1.35;

        /// <summary>
        /// Converts <paramref name="text"/> into a collection of 2D line primitives,
        /// positioned at <paramref name="origin"/>, scaled to <paramref name="height"/>,
        /// and rotated by <paramref name="rotationRad"/> radians CCW.
        /// </summary>
        public static List<PrimitiveDto> TextToPrimitives(
            string text,
            Pt2 origin,
            double height,
            double rotationRad = 0.0,
            string handle = null)
        {
            var result = new List<PrimitiveDto>();
            if (string.IsNullOrEmpty(text) || height <= 1e-6 || origin == null)
                return result;

            double cos = Math.Cos(rotationRad);
            double sin = Math.Sin(rotationRad);

            // Perpendicular direction vector pointing down for subsequent lines
            // Normal (dx, dy) = (-sin, cos); down direction = (sin, -cos)
            double lineDx = sin * height * DefaultLineSpacing;
            double lineDy = -cos * height * DefaultLineSpacing;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx];
                double startX = origin.X + lineIdx * lineDx;
                double startY = origin.Y + lineIdx * lineDy;

                double curOffset = 0.0;

                for (int charIdx = 0; charIdx < line.Length; charIdx++)
                {
                    char ch = line[charIdx];

                    if (ch == ' ')
                    {
                        curOffset += height * 0.4;
                        continue;
                    }

                    if (ch == '\t')
                    {
                        curOffset += height * 1.5;
                        continue;
                    }

                    bool isCjk = ch >= 0x2E80 && ch <= 0x9FFF;
                    double charAdvance = isCjk ? height * 0.95 : height * DefaultCharAdvance;

                    var strokes = GetStrokesForChar(ch);
                    if (strokes != null && strokes.Length > 0)
                    {
                        foreach (var poly in strokes)
                        {
                            if (poly == null || poly.Length < 4) continue;
                            for (int i = 0; i + 3 < poly.Length; i += 2)
                            {
                                double lx1 = curOffset + poly[i] * height;
                                double ly1 = poly[i + 1] * height;
                                double lx2 = curOffset + poly[i + 2] * height;
                                double ly2 = poly[i + 3] * height;

                                double wx1 = startX + (lx1 * cos - ly1 * sin);
                                double wy1 = startY + (lx1 * sin + ly1 * cos);
                                double wx2 = startX + (lx2 * cos - ly2 * sin);
                                double wy2 = startY + (lx2 * sin + ly2 * cos);

                                result.Add(PrimitiveDto.MakeLine(new Pt2(wx1, wy1), new Pt2(wx2, wy2), handle));
                            }
                        }
                    }
                    else if (isCjk)
                    {
                        // Clean glyph box outline for CJK characters: 4 perimeter lines + horizontal center bar
                        double w = height * 0.8;
                        double h = height * 0.85;

                        double lx0 = curOffset + 0.05 * height;
                        double ly0 = 0.05 * height;
                        double lx1 = lx0 + w;
                        double ly1 = ly0 + h;
                        double lmidY = ly0 + h * 0.5;

                        AddWorldLine(result, startX, startY, cos, sin, lx0, ly0, lx1, ly0, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx1, ly0, lx1, ly1, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx1, ly1, lx0, ly1, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx0, ly1, lx0, ly0, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx0, lmidY, lx1, lmidY, handle);
                    }
                    else
                    {
                        // Fallback square marker for unrecognized glyphs
                        double s = height * 0.5;
                        double lx0 = curOffset;
                        double ly0 = 0.0;
                        AddWorldLine(result, startX, startY, cos, sin, lx0, ly0, lx0 + s, ly0, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx0 + s, ly0, lx0 + s, ly0 + s, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx0 + s, ly0 + s, lx0, ly0 + s, handle);
                        AddWorldLine(result, startX, startY, cos, sin, lx0, ly0 + s, lx0, ly0, handle);
                    }

                    curOffset += charAdvance;
                }
            }

            return result;
        }

        private static void AddWorldLine(
            List<PrimitiveDto> list,
            double ox, double oy, double cos, double sin,
            double lx1, double ly1, double lx2, double ly2,
            string handle)
        {
            double wx1 = ox + (lx1 * cos - ly1 * sin);
            double wy1 = oy + (lx1 * sin + ly1 * cos);
            double wx2 = ox + (lx2 * cos - ly2 * sin);
            double wy2 = oy + (lx2 * sin + ly2 * cos);
            list.Add(PrimitiveDto.MakeLine(new Pt2(wx1, wy1), new Pt2(wx2, wy2), handle));
        }

        // Each sub-array is a polyline: [x0, y0, x1, y1, x2, y2, ...]
        private static double[][] GetStrokesForChar(char ch)
        {
            ch = char.ToUpperInvariant(ch);

            switch (ch)
            {
                // Numbers
                case '0':
                    return new[]
                    {
                        new[] { 0.1, 0.0, 0.5, 0.0, 0.6, 0.2, 0.6, 0.8, 0.5, 1.0, 0.1, 1.0, 0.0, 0.8, 0.0, 0.2, 0.1, 0.0 },
                        new[] { 0.1, 0.15, 0.5, 0.85 }
                    };
                case '1':
                    return new[]
                    {
                        new[] { 0.1, 0.8, 0.35, 1.0, 0.35, 0.0 },
                        new[] { 0.1, 0.0, 0.6, 0.0 }
                    };
                case '2':
                    return new[]
                    {
                        new[] { 0.05, 0.8, 0.2, 1.0, 0.45, 1.0, 0.6, 0.8, 0.6, 0.6, 0.0, 0.0, 0.6, 0.0 }
                    };
                case '3':
                    return new[]
                    {
                        new[] { 0.05, 1.0, 0.6, 1.0, 0.3, 0.55, 0.5, 0.55, 0.6, 0.4, 0.6, 0.15, 0.45, 0.0, 0.05, 0.0 }
                    };
                case '4':
                    return new[]
                    {
                        new[] { 0.45, 0.0, 0.45, 1.0, 0.05, 0.35, 0.6, 0.35 }
                    };
                case '5':
                    return new[]
                    {
                        new[] { 0.55, 1.0, 0.08, 1.0, 0.05, 0.55, 0.45, 0.55, 0.6, 0.4, 0.6, 0.15, 0.45, 0.0, 0.05, 0.0 }
                    };
                case '6':
                    return new[]
                    {
                        new[] { 0.5, 0.95, 0.2, 0.7, 0.05, 0.4, 0.05, 0.15, 0.2, 0.0, 0.45, 0.0, 0.6, 0.15, 0.6, 0.4, 0.45, 0.55, 0.05, 0.4 }
                    };
                case '7':
                    return new[]
                    {
                        new[] { 0.05, 1.0, 0.6, 1.0, 0.2, 0.0 },
                        new[] { 0.15, 0.5, 0.45, 0.5 }
                    };
                case '8':
                    return new[]
                    {
                        new[] { 0.2, 0.5, 0.05, 0.7, 0.05, 0.85, 0.2, 1.0, 0.4, 1.0, 0.55, 0.85, 0.55, 0.7, 0.2, 0.5,
                                0.05, 0.35, 0.05, 0.15, 0.2, 0.0, 0.4, 0.0, 0.55, 0.15, 0.55, 0.35, 0.2, 0.5 }
                    };
                case '9':
                    return new[]
                    {
                        new[] { 0.55, 0.6, 0.15, 0.6, 0.05, 0.75, 0.05, 0.85, 0.2, 1.0, 0.4, 1.0, 0.55, 0.85, 0.55, 0.25, 0.4, 0.0, 0.1, 0.05 }
                    };

                // Letters
                case 'A':
                    return new[]
                    {
                        new[] { 0.05, 0.0, 0.3, 1.0, 0.55, 0.0 },
                        new[] { 0.15, 0.38, 0.45, 0.38 }
                    };
                case 'B':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0, 0.42, 1.0, 0.55, 0.85, 0.42, 0.5, 0.08, 0.5 },
                        new[] { 0.42, 0.5, 0.58, 0.35, 0.58, 0.15, 0.45, 0.0, 0.08, 0.0 }
                    };
                case 'C':
                    return new[]
                    {
                        new[] { 0.58, 0.85, 0.42, 1.0, 0.2, 1.0, 0.05, 0.8, 0.05, 0.2, 0.2, 0.0, 0.42, 0.0, 0.58, 0.15 }
                    };
                case 'D':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0, 0.35, 1.0, 0.58, 0.8, 0.58, 0.2, 0.35, 0.0, 0.08, 0.0 }
                    };
                case 'E':
                    return new[]
                    {
                        new[] { 0.55, 1.0, 0.08, 1.0, 0.08, 0.0, 0.58, 0.0 },
                        new[] { 0.08, 0.5, 0.45, 0.5 }
                    };
                case 'F':
                    return new[]
                    {
                        new[] { 0.55, 1.0, 0.08, 1.0, 0.08, 0.0 },
                        new[] { 0.08, 0.5, 0.45, 0.5 }
                    };
                case 'G':
                    return new[]
                    {
                        new[] { 0.58, 0.85, 0.42, 1.0, 0.2, 1.0, 0.05, 0.8, 0.05, 0.2, 0.2, 0.0, 0.45, 0.0, 0.58, 0.15, 0.58, 0.45, 0.35, 0.45 }
                    };
                case 'H':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0 },
                        new[] { 0.55, 0.0, 0.55, 1.0 },
                        new[] { 0.08, 0.5, 0.55, 0.5 }
                    };
                case 'I':
                    return new[]
                    {
                        new[] { 0.3, 0.0, 0.3, 1.0 },
                        new[] { 0.12, 1.0, 0.48, 1.0 },
                        new[] { 0.12, 0.0, 0.48, 0.0 }
                    };
                case 'J':
                    return new[]
                    {
                        new[] { 0.12, 1.0, 0.48, 1.0 },
                        new[] { 0.4, 1.0, 0.4, 0.2, 0.3, 0.0, 0.12, 0.0, 0.05, 0.15 }
                    };
                case 'K':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0 },
                        new[] { 0.55, 1.0, 0.08, 0.45, 0.58, 0.0 }
                    };
                case 'L':
                    return new[]
                    {
                        new[] { 0.08, 1.0, 0.08, 0.0, 0.55, 0.0 }
                    };
                case 'M':
                    return new[]
                    {
                        new[] { 0.06, 0.0, 0.06, 1.0, 0.31, 0.4, 0.56, 1.0, 0.56, 0.0 }
                    };
                case 'N':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0, 0.55, 0.0, 0.55, 1.0 }
                    };
                case 'O':
                    return new[]
                    {
                        new[] { 0.2, 0.0, 0.42, 0.0, 0.58, 0.2, 0.58, 0.8, 0.42, 1.0, 0.2, 1.0, 0.05, 0.8, 0.05, 0.2, 0.2, 0.0 }
                    };
                case 'P':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0, 0.42, 1.0, 0.58, 0.85, 0.58, 0.65, 0.42, 0.5, 0.08, 0.5 }
                    };
                case 'Q':
                    return new[]
                    {
                        new[] { 0.2, 0.0, 0.42, 0.0, 0.58, 0.2, 0.58, 0.8, 0.42, 1.0, 0.2, 1.0, 0.05, 0.8, 0.05, 0.2, 0.2, 0.0 },
                        new[] { 0.35, 0.25, 0.62, -0.08 }
                    };
                case 'R':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.08, 1.0, 0.42, 1.0, 0.58, 0.85, 0.58, 0.65, 0.42, 0.5, 0.08, 0.5 },
                        new[] { 0.35, 0.5, 0.58, 0.0 }
                    };
                case 'S':
                    return new[]
                    {
                        new[] { 0.55, 0.85, 0.4, 1.0, 0.2, 1.0, 0.05, 0.85, 0.05, 0.65, 0.2, 0.5, 0.42, 0.5, 0.58, 0.35, 0.58, 0.15, 0.42, 0.0, 0.2, 0.0, 0.05, 0.15 }
                    };
                case 'T':
                    return new[]
                    {
                        new[] { 0.05, 1.0, 0.58, 1.0 },
                        new[] { 0.315, 1.0, 0.315, 0.0 }
                    };
                case 'U':
                    return new[]
                    {
                        new[] { 0.08, 1.0, 0.08, 0.25, 0.22, 0.0, 0.42, 0.0, 0.56, 0.25, 0.56, 1.0 }
                    };
                case 'V':
                    return new[]
                    {
                        new[] { 0.05, 1.0, 0.315, 0.0, 0.58, 1.0 }
                    };
                case 'W':
                    return new[]
                    {
                        new[] { 0.05, 1.0, 0.18, 0.0, 0.32, 0.6, 0.46, 0.0, 0.59, 1.0 }
                    };
                case 'X':
                    return new[]
                    {
                        new[] { 0.06, 0.0, 0.56, 1.0 },
                        new[] { 0.06, 1.0, 0.56, 0.0 }
                    };
                case 'Y':
                    return new[]
                    {
                        new[] { 0.06, 1.0, 0.315, 0.5, 0.56, 1.0 },
                        new[] { 0.315, 0.5, 0.315, 0.0 }
                    };
                case 'Z':
                    return new[]
                    {
                        new[] { 0.06, 1.0, 0.56, 1.0, 0.06, 0.0, 0.56, 0.0 }
                    };

                // Punctuation & symbols
                case '-':
                    return new[]
                    {
                        new[] { 0.08, 0.5, 0.52, 0.5 }
                    };
                case '+':
                    return new[]
                    {
                        new[] { 0.08, 0.5, 0.52, 0.5 },
                        new[] { 0.3, 0.25, 0.3, 0.75 }
                    };
                case '=':
                    return new[]
                    {
                        new[] { 0.08, 0.62, 0.52, 0.62 },
                        new[] { 0.08, 0.38, 0.52, 0.38 }
                    };
                case '/':
                    return new[]
                    {
                        new[] { 0.08, 0.0, 0.52, 1.0 }
                    };
                case '\\':
                    return new[]
                    {
                        new[] { 0.08, 1.0, 0.52, 0.0 }
                    };
                case '.':
                    return new[]
                    {
                        new[] { 0.25, 0.0, 0.35, 0.0, 0.35, 0.1, 0.25, 0.1, 0.25, 0.0 }
                    };
                case ',':
                    return new[]
                    {
                        new[] { 0.3, 0.1, 0.35, 0.1, 0.22, -0.15 }
                    };
                case ':':
                    return new[]
                    {
                        new[] { 0.25, 0.25, 0.35, 0.25, 0.35, 0.35, 0.25, 0.35, 0.25, 0.25 },
                        new[] { 0.25, 0.65, 0.35, 0.65, 0.35, 0.75, 0.25, 0.75, 0.25, 0.65 }
                    };
                case ';':
                    return new[]
                    {
                        new[] { 0.3, 0.25, 0.35, 0.25, 0.22, 0.05 },
                        new[] { 0.25, 0.65, 0.35, 0.65, 0.35, 0.75, 0.25, 0.75, 0.25, 0.65 }
                    };
                case '_':
                    return new[]
                    {
                        new[] { 0.0, -0.1, 0.6, -0.1 }
                    };
                case '(':
                    return new[]
                    {
                        new[] { 0.45, 1.0, 0.2, 0.75, 0.2, 0.25, 0.45, 0.0 }
                    };
                case ')':
                    return new[]
                    {
                        new[] { 0.15, 1.0, 0.4, 0.75, 0.4, 0.25, 0.15, 0.0 }
                    };
                case '[':
                    return new[]
                    {
                        new[] { 0.45, 1.0, 0.2, 1.0, 0.2, 0.0, 0.45, 0.0 }
                    };
                case ']':
                    return new[]
                    {
                        new[] { 0.15, 1.0, 0.4, 1.0, 0.4, 0.0, 0.15, 0.0 }
                    };
                case '<':
                    return new[]
                    {
                        new[] { 0.5, 0.85, 0.1, 0.5, 0.5, 0.15 }
                    };
                case '>':
                    return new[]
                    {
                        new[] { 0.1, 0.85, 0.5, 0.5, 0.1, 0.15 }
                    };
                case '#':
                    return new[]
                    {
                        new[] { 0.2, 0.0, 0.26, 1.0 },
                        new[] { 0.42, 0.0, 0.48, 1.0 },
                        new[] { 0.05, 0.35, 0.6, 0.35 },
                        new[] { 0.05, 0.65, 0.6, 0.65 }
                    };
                case '*':
                    return new[]
                    {
                        new[] { 0.1, 0.5, 0.5, 0.5 },
                        new[] { 0.16, 0.22, 0.44, 0.78 },
                        new[] { 0.16, 0.78, 0.44, 0.22 }
                    };
                case '%':
                    return new[]
                    {
                        new[] { 0.1, 0.1, 0.55, 0.9 },
                        new[] { 0.15, 0.75, 0.25, 0.75, 0.25, 0.85, 0.15, 0.85, 0.15, 0.75 },
                        new[] { 0.4, 0.15, 0.5, 0.15, 0.5, 0.25, 0.4, 0.25, 0.4, 0.15 }
                    };
                case '!':
                    return new[]
                    {
                        new[] { 0.3, 1.0, 0.3, 0.3 },
                        new[] { 0.25, 0.0, 0.35, 0.0, 0.35, 0.1, 0.25, 0.1, 0.25, 0.0 }
                    };
                case '?':
                    return new[]
                    {
                        new[] { 0.1, 0.8, 0.2, 1.0, 0.42, 1.0, 0.55, 0.8, 0.55, 0.65, 0.3, 0.45, 0.3, 0.3 },
                        new[] { 0.25, 0.0, 0.35, 0.0, 0.35, 0.1, 0.25, 0.1, 0.25, 0.0 }
                    };
                case '"':
                    return new[]
                    {
                        new[] { 0.2, 1.0, 0.2, 0.7 },
                        new[] { 0.4, 1.0, 0.4, 0.7 }
                    };
                case '\'':
                    return new[]
                    {
                        new[] { 0.3, 1.0, 0.3, 0.7 }
                    };
                case '&':
                    return new[]
                    {
                        new[] { 0.55, 0.1, 0.4, 0.0, 0.2, 0.0, 0.08, 0.18, 0.08, 0.38, 0.35, 0.65, 0.35, 0.82, 0.22, 0.95, 0.12, 0.82, 0.12, 0.68, 0.55, 0.0 }
                    };
                case '@':
                    return new[]
                    {
                        new[] { 0.55, 0.35, 0.45, 0.35, 0.35, 0.35, 0.25, 0.45, 0.25, 0.55, 0.35, 0.65, 0.45, 0.65, 0.45, 0.3, 0.55, 0.3, 0.55, 0.75, 0.4, 0.95, 0.18, 0.95, 0.05, 0.75, 0.05, 0.35, 0.2, 0.05, 0.45, 0.05 }
                    };
                case '|':
                    return new[]
                    {
                        new[] { 0.3, -0.15, 0.3, 1.15 }
                    };
                case '~':
                    return new[]
                    {
                        new[] { 0.08, 0.5, 0.2, 0.65, 0.35, 0.5, 0.5, 0.65 }
                    };
                default:
                    return null;
            }
        }
    }
}

