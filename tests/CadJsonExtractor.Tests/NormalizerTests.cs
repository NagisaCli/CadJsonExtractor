using System.Collections.Generic;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Geometry;
using Xunit;

namespace CadJsonExtractor.Tests
{
    public class NormalizerTests
    {
        [Fact]
        public void Normalizer_RemovesZeroLengthSegments()
        {
            var prims = new List<PrimitiveDto>
            {
                PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(0, 0.001), "Z1"),
                PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(100, 0), "L1")
            };

            var opts = new NormalizeOptions { ZeroLengthTolerance = 0.01 };
            var cleaned = Normalizer.Normalize(prims, opts, out var rep);

            Assert.Single(cleaned);
            Assert.Equal(1, rep.ZeroLengthRemoved);
        }

        [Fact]
        public void Normalizer_RemovesExactDuplicates()
        {
            var prims = new List<PrimitiveDto>
            {
                PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(100, 0), "L1"),
                PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(100, 0), "L2")
            };

            var opts = new NormalizeOptions();
            var cleaned = Normalizer.Normalize(prims, opts, out var rep);

            Assert.Single(cleaned);
            Assert.Equal(1, rep.DuplicatesRemoved);
        }

        [Fact]
        public void Normalizer_MergesCollinearSegments()
        {
            var prims = new List<PrimitiveDto>
            {
                PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(50, 0), "S1"),
                PrimitiveDto.MakeLine(new Pt2(50, 0), new Pt2(100, 0), "S2")
            };

            var opts = new NormalizeOptions { MergeCollinear = true };
            var cleaned = Normalizer.Normalize(prims, opts, out var rep);

            Assert.Single(cleaned);
            Assert.Equal(0, cleaned[0].Start.X);
            Assert.Equal(100, cleaned[0].End.X);
        }
    }
}
