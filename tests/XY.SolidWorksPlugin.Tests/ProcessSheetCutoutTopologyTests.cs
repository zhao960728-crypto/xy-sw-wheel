using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessSheetCutoutTopologyTests
{
    [Fact]
    public void OppositeOpeningsShareActualWallsAndCountOneThroughHole()
    {
        var top = Skin("top", 2, new[] { "wall" }); var bottom = Skin("bottom", 0, new[] { "wall" });
        var result = ProcessSheetCutoutTopology.Analyze(new[] { top, bottom, Wall("wall") }, 2);
        Assert.True(result.Reliable, result.Evidence); Assert.Equal(1, result.Count);
    }
    [Fact]
    public void SeparateHolesOnOneFaceCountSeparately()
    {
        var top = Skin("top", 2, new[] { "a" }, new[] { "b" }); var bottom = Skin("bottom", 0, new[] { "a" }, new[] { "b" });
        var result = ProcessSheetCutoutTopology.Analyze(new[] { top, bottom, Wall("a"), Wall("b") }, 2);
        Assert.True(result.Reliable, result.Evidence); Assert.Equal(2, result.Count);
    }
    [Fact]
    public void DifferentFlangeNormalsDoNotLoseOnePlanesOpening()
    {
        var top = Skin("top", 2, new[] { "a" }); var bottom = Skin("bottom", 0, new[] { "a" });
        var left = Skin("left", 2, new[] { "b" }); var right = Skin("right", 0, new[] { "b" });
        left.Normal = right.Normal = new[] { 1d, 0, 0 }; left.PointMm = new[] { 2d, 0, 0 }; right.PointMm = new[] { 0d, 0, 0 };
        var result = ProcessSheetCutoutTopology.Analyze(new[] { top, bottom, left, right, Wall("a"), Wall("b") }, 2, true);
        Assert.Equal(2, result.Count); Assert.False(result.Reliable); Assert.Contains("折弯曲面", result.Evidence);
    }
    [Fact]
    public void CoaxialOpposedBlindHolesStaySeparateBecauseTheirWallsAreDisconnected()
    {
        var result = ProcessSheetCutoutTopology.Analyze(new[] { Skin("top", 8, new[] { "topwall" }), Skin("bottom", 0, new[] { "bottomwall" }), Wall("topwall", "topfloor"), Wall("topfloor", "topwall"), Wall("bottomwall", "bottomfloor"), Wall("bottomfloor", "bottomwall") }, 8);
        Assert.True(result.Reliable, result.Evidence); Assert.Equal(2, result.Count);
    }
    [Fact]
    public void CounterboreShoulderConnectsTheTwoDifferentDiameterWalls()
    {
        var shoulder = Skin("shoulder", 1, new[] { "inner" }); shoulder.Neighbours.Add("inner"); shoulder.Neighbours.Add("outer");
        var result = ProcessSheetCutoutTopology.Analyze(new[] { Skin("top", 2, new[] { "outer" }), Skin("bottom", 0, new[] { "inner" }), shoulder, Wall("inner", "shoulder"), Wall("outer", "shoulder") }, 2);
        Assert.True(result.Reliable, result.Evidence); Assert.Equal(1, result.Count);
    }
    [Fact]
    public void MissingWallReferenceDoesNotBecomeAnAuthoritativeZero()
    {
        var result = ProcessSheetCutoutTopology.Analyze(new[] { Skin("top", 2, new[] { "missing" }), Skin("bottom", 0) }, 2);
        Assert.False(result.Reliable); Assert.Contains("邻接面", result.Evidence);
    }
    [Fact]
    public void UnverifiedCutoutTotalSurvivesSnapshotAndIsNeverWritten()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, CutoutCount = 3, CutoutCountReliable = false, CutoutCountEvidence = "跨折弯待复核" } };
        input = ProcessRecognitionSnapshot.Decode(ProcessRecognitionSnapshot.Create(input, Array.Empty<RecognizedProcess>(), true).Encode()).Input.Restore();
        Assert.False(input.Geometry.CutoutCountReliable); Assert.Equal("跨折弯待复核", input.Geometry.CutoutCountEvidence);
        var config = new ProcessRecognitionConfig();
        Assert.DoesNotContain(ProcessRecognitionService.BuildPropertyValues(new[] { new RecognizedProcess { Name = "激光下料" } }, input.Geometry, config), pair => pair.Key == config.CutoutCountPropertyName);
        Assert.Contains("总数未确认", ProcessAnalysisWindow.Describe(config, input, true, null));
    }
    private static ProcessSheetCutoutTopology.FaceFact Skin(string key, double z, params string[][] loops)
    {
        var face = new ProcessSheetCutoutTopology.FaceFact { Key = key, Normal = new[] { 0d, 0, 1 }, PointMm = new[] { 0d, 0, z } };
        face.InnerWalls.AddRange(loops); foreach (var wall in loops.SelectMany(loop => loop)) face.Neighbours.Add(wall); return face;
    }
    private static ProcessSheetCutoutTopology.FaceFact Wall(string key, params string[] neighbours)
    { var face = new ProcessSheetCutoutTopology.FaceFact { Key = key }; foreach (var neighbour in neighbours) face.Neighbours.Add(neighbour); return face; }
}
