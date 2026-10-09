using System;
using System.Collections.Generic;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Geometry;
using Xunit;

namespace CadJsonExtractor.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void Tessellator_FromBulge_CreatesValidArc()
        {
            var p1 = new Pt2(0, 0);
            var p2 = new Pt2(100, 0);
            double bulge = 1.0; // Semicircle

            var arc = Tessellator.FromBulge(p1, p2, bulge, "H1");
            Assert.NotNull(arc);
            Assert.Equal(PrimitiveKind.Arc, arc.Kind);
            Assert.Equal(50.0, arc.Radius, 3);
            Assert.Equal(50.0, arc.Centre.X, 3);
        }

        [Fact]
        public void Tessellator_ToLines_RespectsMaxChordError()
        {
            var circle = PrimitiveDto.MakeCircle(new Pt2(0, 0), 100.0, "C1");
            var lines = Tessellator.ToLines(circle, maxChordError: 1.0);

            Assert.True(lines.Count >= Tessellator.MinCircleSegments);
            foreach (var line in lines)
            {
                Assert.Equal(PrimitiveKind.Line, line.Kind);
                Assert.NotNull(line.Start);
                Assert.NotNull(line.End);
            }
        }

        [Fact]
        public void StrokeFont_RendersEnglishLetters()
        {
            var prims = StrokeFont.TextToPrimitives("PUMP 101", new Pt2(10, 20), 50.0, 0.0, "T1");
            Assert.NotEmpty(prims);
            foreach (var p in prims)
            {
                Assert.Equal(PrimitiveKind.Line, p.Kind);
            }
        }
    }
}
