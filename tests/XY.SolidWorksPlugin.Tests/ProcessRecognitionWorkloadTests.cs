using System;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRecognitionWorkloadTests
{
    [Theory]
    [InlineData(1, 24, 12, true)]
    [InlineData(1, 400, 12, true)]
    [InlineData(1, 401, 12, false)]
    [InlineData(9, 100, 12, false)]
    [InlineData(1, 24, 251, false)]
    public void AutomaticDeepHasExplicitCountBounds(int bodies, int faces, int features, bool allowed)
        => Assert.Equal(allowed, ProcessRecognitionWorkload.FromCounts(bodies, faces, features: features).AllowAutomaticDeep);
    [Fact]
    public void UnknownWorkloadNeverStartsAutomaticDeepAndExplicitDecisionIsRequired()
    {
        var workload = ProcessRecognitionWorkload.FromCounts(0, 0, known: false);
        Assert.False(workload.AllowAutomaticDeep); Assert.True(workload.RequiresConfirmation);
        int asked = 0; Assert.False(workload.Confirm(null!, "深度识别", (_, operation) => { asked++; Assert.Equal("深度识别", operation); return false; })); Assert.Equal(1, asked);
        Assert.Contains("无法可靠估计", workload.DescribeEstimate());
    }
    [Fact]
    public void SmallReadDoesNotInterruptUserAndLargeEstimateStatesItsUncertainty()
    {
        var small = ProcessRecognitionWorkload.FromCounts(1, 24); Assert.True(small.Confirm(null!, "扫描", (_, __) => throw new InvalidOperationException("Small recognition should not prompt")));
        var large = ProcessRecognitionWorkload.FromCounts(20, 2400); Assert.True(large.RequiresConfirmation); Assert.False(large.AllowAutomaticDeep);
        Assert.True(large.EstimatedMaxSeconds > large.EstimatedMinSeconds); Assert.Contains("仅供参考", large.DescribeEstimate());
        Assert.Contains("1～5 秒", large.DescribeOperation("当前零件深度识别"));
        Assert.Contains("部分结果", large.DescribeOperation("当前零件深度识别"));
        Assert.Contains("可取消", large.DescribeOperation("工艺孔识别"));
        Assert.True(ProcessRecognitionWorkload.FromCounts(20, 100000).EstimatedMaxSeconds < 3600);
    }
    [Fact]
    public void HoleWriteInvalidatesCompleteGeometryRecursivelyAndPreservesKnownRoutes()
    {
        var body = new ProcessGeometryFact { GeometryComplete = true, BendCount = 2 };
        var geometry = new ProcessGeometryFact { GeometryComplete = true, BendCount = 2, BodyFacts = new[] { body } };
        ProcessHoleSuggestionPolicy.MarkGeometryPending(geometry);
        Assert.False(geometry.GeometryComplete); Assert.False(body.GeometryComplete); Assert.Equal(2, body.BendCount);
    }
    [Fact]
    public void ConfirmedHoleStepsMergeBeforeBendingWithoutChangingExistingRelativeOrderOrDuplicatingAlias()
    {
        var current = new[] { new RecognizedProcess { Name = "下料", Evidence = "人工" }, new RecognizedProcess { Name = "攻丝" }, new RecognizedProcess { Name = "折弯" }, new RecognizedProcess { Name = "焊接" }, new RecognizedProcess { Name = "终检" } };
        var facts = new[] { new ProcessHoleFact { ProcessName = "压铆", IsConfirmed = true, Evidence = "原生工艺记录" }, new ProcessHoleFact { ProcessName = "攻牙", IsConfirmed = true }, new ProcessHoleFact { ProcessName = "沉孔", IsConfirmed = false } };
        var merged = ProcessHoleSuggestionPolicy.MergeMissing(current, facts, new ProcessRecognitionConfig());
        Assert.Equal(new[] { "下料", "攻丝", "压铆", "折弯", "焊接", "终检" }, merged.Select(row => row.Name));
        Assert.Equal("人工", merged[0].Evidence); Assert.Equal(5, current.Length);
        Assert.Equal(merged.Select(row => row.Name), ProcessHoleSuggestionPolicy.MergeMissing(merged, facts, new ProcessRecognitionConfig()).Select(row => row.Name));
    }

    [Fact]
    public void HardwareSpecificHoleNamesCollapseToTheirManufacturingStep()
    {
        var current = new[] { new RecognizedProcess { Name = "钻孔" }, new RecognizedProcess { Name = "压铆" }, new RecognizedProcess { Name = "折弯" } };
        var facts = new[] { new ProcessHoleFact { ProcessName = "压铆螺母", IsConfirmed = true, Evidence = "PEM" } };

        var merged = ProcessHoleSuggestionPolicy.MergeMissing(current, facts, new ProcessRecognitionConfig());

        Assert.Equal(new[] { "钻孔", "压铆", "折弯" }, merged.Select(row => row.Name));
        Assert.DoesNotContain(merged, row => row.Name == "压铆螺母");
    }
}
