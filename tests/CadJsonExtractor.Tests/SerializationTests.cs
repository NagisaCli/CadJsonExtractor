using System;
using System.Collections.Generic;
using CadJsonExtractor.Core.Contract;
using CadJsonExtractor.Core.Serialization;
using Xunit;

namespace CadJsonExtractor.Tests
{
    public class SerializationTests
    {
        [Fact]
        public void Payload_Roundtrip_PreservesData()
        {
            var payload = new CadJsonPayload
            {
                SourceDrawing = "FloorPlan_Level1.dwg",
                SourceApplication = "AutoCAD 2025",
                SourceUnits = CadUnits.Millimetres,
                BatchBasePoint = new Pt2(100.5, 200.75),
                ExporterVersion = "1.0.0"
            };

            var group = new CadGroupDto
            {
                SourceId = "GROUP-01",
                SourceBlockName = "CENTRIFUGAL_PUMP",
                SourceTag = "P-101A",
                SuggestedName = "/CAD-P-101A",
                InsertionPoint = new Pt2(1000, 2000),
                LocalBasePoint = new Pt2(1000, 2000),
                GroupingMode = GroupingMode.BlockReference
            };

            group.Primitives.Add(PrimitiveDto.MakeLine(new Pt2(0, 0), new Pt2(100, 0), "H101"));
            group.Primitives.Add(PrimitiveDto.MakeCircle(new Pt2(50, 50), 25, "H102"));
            group.Primitives.Add(PrimitiveDto.MakeArc(new Pt2(50, 50), 30, 0, Math.PI / 2, "H103"));

            payload.Groups.Add(group);

            string json = PayloadSerializer.Serialize(payload, indented: true);
            Assert.False(string.IsNullOrWhiteSpace(json));

            var roundtrip = PayloadSerializer.TryDeserialize(json, out var vr);
            Assert.True(vr.IsValid, string.Join("; ", vr.Errors));
            Assert.NotNull(roundtrip);
            Assert.Equal("FloorPlan_Level1.dwg", roundtrip.SourceDrawing);
            Assert.Single(roundtrip.Groups);
            Assert.Equal("CENTRIFUGAL_PUMP", roundtrip.Groups[0].SourceBlockName);
            Assert.Equal(3, roundtrip.Groups[0].Primitives.Count);
            Assert.Equal(PrimitiveKind.Line, roundtrip.Groups[0].Primitives[0].Kind);
            Assert.Equal(PrimitiveKind.Circle, roundtrip.Groups[0].Primitives[1].Kind);
            Assert.Equal(PrimitiveKind.Arc, roundtrip.Groups[0].Primitives[2].Kind);
        }

        [Fact]
        public void Validation_RejectsNullAndEmptyGroups()
        {
            var empty = new CadJsonPayload { Groups = new List<CadGroupDto>() };
            var res = PayloadSerializer.Validate(empty);
            Assert.False(res.IsValid);
            Assert.Contains(res.Errors, e => e.Contains("no groups"));
        }

        [Fact]
        public void Envelope_Roundtrip_Works()
        {
            var env = new PayloadFileEnvelope
            {
                FilePath = "C:\\Temp\\test.json",
                FileSize = 123456,
                Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            };

            string json = PayloadSerializer.SerializeEnvelope(env);
            var parsed = PayloadSerializer.TryDeserializeEnvelope(json);

            Assert.NotNull(parsed);
            Assert.Equal(env.FilePath, parsed.FilePath);
            Assert.Equal(env.FileSize, parsed.FileSize);
            Assert.Equal(env.Sha256, parsed.Sha256);
        }
    }
}
