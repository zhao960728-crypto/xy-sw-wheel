using System;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class ProcessManufacturingPlanTests
{
    [Fact]
    public void MixedSheetAndTubeRequiresBothStockRoutesThenBendingThenWelding()
    {
        var tube = new ProcessGeometryFact { BodyName = "框架", SolidBodyCount = 1, StraightTubeBodyCount = 1, ThicknessMm = 2 };
        var sheet = new ProcessGeometryFact { BodyName = "折板", SolidBodyCount = 1, SheetBodyCount = 1, BendCount = 2, ThicknessMm = 2 };
        var input = new ProcessRecognitionInput { IsPart = true, PartType = PartTypes.Mixed,
            Geometry = ProcessGeometryAnalyzer.Merge(new[] { tube, sheet }) };
        var processes = ProcessRecognitionService.Recognize(input, true).Select(p => p.Name).ToList();
        Assert.Contains("激光下料", processes);
        Assert.Contains("锯切下料", processes);
        Assert.True(processes.IndexOf("折弯") < processes.IndexOf("焊接"));
        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());
        Assert.Equal(2, plan.BodyRoutes.Count);
        Assert.DoesNotContain("折弯", plan.BodyRoutes[0].Processes);
        Assert.Contains("折弯", plan.BodyRoutes[1].Processes);
        Assert.Contains("定位组对", plan.Summary);
    }

    [Fact]
    public void MultipleBodiesAloneDoNotProveWelding()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Geometry = new ProcessGeometryFact { SolidBodyCount = 2 } };
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), p => p.Name == "焊接");
        Assert.Contains("没有明确焊接证据", ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig()).Summary);
    }

    [Fact]
    public void OneStructuralTubeDoesNotProveWelding()
    {
        var input = new ProcessRecognitionInput { IsPart = true,
            Features = new[] { new ProcessFeatureFact { TypeName = "StructuralMember", Name = "结构构件1" } },
            Geometry = new ProcessGeometryFact { SolidBodyCount = 1, StraightTubeBodyCount = 1 } };
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), p => p.Name == "焊接");
    }

    [Fact]
    public void SingleBodyBentThenWeldedDoesNotRequireMultibodyCount()
    {
        var input = new ProcessRecognitionInput { IsPart = true, ProcessType = "焊接封角",
            Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 3 } };
        var processes = ProcessRecognitionService.Recognize(input, true).Select(p => p.Name).ToList();
        Assert.True(processes.IndexOf("折弯") >= 0 && processes.IndexOf("折弯") < processes.IndexOf("焊接封角"));
        Assert.Contains("折弯", ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig()).Summary);
    }

    [Theory]
    [InlineData("铰链总成")]
    [InlineData("门锁总成")]
    [InlineData("脚轮总成")]
    public void PurchasedAssemblyUsesSupplyFlowAndRemainsUnmarkedUntilConfirmation(string title)
    {
        var input = new ProcessRecognitionInput { IsAssembly = true, DocumentTitle = title };
        Assert.True(PurchasedPartPolicy.IsPurchased(input));
        Assert.Contains("尚未标记", PurchasedPartPolicy.Display(input));
        Assert.Equal(new[] { "外购", "来料检验", "入库" }, ProcessRecognitionService.Recognize(input, true).Select(p => p.Name));
        Assert.Equal("", input.Purchase);
        input.Purchase = "否";
        Assert.False(PurchasedPartPolicy.IsPurchased(input));
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), p => p.Name == "外购");
    }

    [Fact]
    public void ProfileSectionExtrusionIsStockWhileCutRemainsMachiningEvidence()
    {
        var input = new ProcessRecognitionInput { IsPart = true, PartType = "角钢",
            Features = new[] { new ProcessFeatureFact { TypeName = "Boss", Name = "拉伸凸台1" } } };
        var steps = ProcessRecognitionService.Recognize(input, false);
        Assert.Contains(steps, p => p.Name == "锯切下料");
        Assert.DoesNotContain(steps, p => p.Name == "铣削");
        input.Features = new[] { new ProcessFeatureFact { TypeName = "Cut", Name = "拉伸切除1" } };
        Assert.Contains(ProcessRecognitionService.Recognize(input, false), p => p.Name == "铣削");
    }

    [Fact]
    public void MultiBodyStructuralMetadataGetsSeparateStockRoutes()
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            Geometry = ProcessGeometryAnalyzer.Merge(new[]
            {
                new ProcessGeometryFact { BodyName = "角钢一", SolidBodyCount = 1, ProfileKind = "角钢", GeometryComplete = true },
                new ProcessGeometryFact { BodyName = "方管二", SolidBodyCount = 1, StraightTubeBodyCount = 1, ProfileKind = "方通", GeometryComplete = true }
            })
        };

        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());

        Assert.Equal(2, plan.BodyRoutes.Count);
        Assert.All(plan.BodyRoutes, route => Assert.Contains("锯切下料", route.Processes));
        Assert.Contains(plan.BodyRoutes, route => route.Type == "角钢");
        Assert.Contains(plan.BodyRoutes, route => route.Type == "方通");
        Assert.Contains(plan.Warnings, warning => warning.Contains("多种型材截面"));
    }

    [Fact]
    public void IncompleteBodyAnalysisIsVisibleAndDoesNotDeclareNoBends()
    {
        var body = new ProcessGeometryFact { BodyName = "未读实体", SolidBodyCount = 1, GeometryComplete = false };
        var input = new ProcessRecognitionInput { IsPart = true, Geometry = ProcessGeometryAnalyzer.Merge(new[] { body }) };
        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());
        Assert.Contains(plan.Warnings, w => w.Contains("尚未完整"));
        Assert.Equal("待确认实体", Assert.Single(plan.BodyRoutes).Type);
    }

    [Fact]
    public void ConfirmedHoleProcessesAreShownInMultiBodyRouteSummary()
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            Geometry = new ProcessGeometryFact { SolidBodyCount = 2 },
            HoleFacts = new[]
            {
                new ProcessHoleFact { ProcessName = "压铆螺母", IsConfirmed = true },
                new ProcessHoleFact { ProcessName = "攻牙", IsConfirmed = true },
                new ProcessHoleFact { ProcessName = "沉孔", IsConfirmed = false }
            }
        };

        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());

        Assert.Contains("已确认孔工艺：攻丝 → 压铆", plan.Summary);
        Assert.DoesNotContain("沉孔", plan.Summary);
    }

    [Fact]
    public void ExistingConfirmedProcessRouteContributesWeldingEvidence()
    {
        var input = new ProcessRecognitionInput
        {
            IsPart = true,
            ExistingProcess = "激光下料>折弯>组焊",
            Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 1 }
        };

        var plan = ProcessManufacturingPlan.Build(input, true, new ProcessRecognitionConfig());

        Assert.Contains("定位组对", plan.Summary);
        Assert.Contains("折弯", plan.Summary);
    }
}
