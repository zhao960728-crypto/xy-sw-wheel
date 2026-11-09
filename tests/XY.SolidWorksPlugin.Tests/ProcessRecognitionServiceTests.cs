using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRecognitionServiceTests
{
	[Theory]
	[InlineData("激光下料>折弯", "-", "激光下料-折弯")]
	[InlineData("激光下料→折弯", "-", "激光下料-折弯")]
	[InlineData("激光下料/折弯", "/", "激光下料/折弯")]
	public void ManualProcessSequenceNormalizesGroupAndUserSeparators(string input, string separator, string expected)
		=> Assert.Equal(expected, ProcessRecognitionService.NormalizeManualProcessSequence(input, separator));

	[Fact]
	public void ManualProcessGroupWritesGroupNameAndEditableSequenceProperties()
	{
		var config = new ProcessRecognitionConfig { WriteCraftDescription = true, Separator = "-" };
		IReadOnlyList<KeyValuePair<string, string>> values = ProcessRecognitionService.BuildManualProcessGroupValues("激光下料-折弯", "钣金件", config);
		Assert.Equal("钣金件", values.Single(pair => pair.Key == config.ProcessTypePropertyName).Value);
		Assert.Equal("激光下料-折弯", values.Single(pair => pair.Key == config.SummaryPropertyName).Value);
		Assert.Contains(values, pair => pair.Key == config.CraftDescriptionPropertyName && pair.Value.Contains("激光下料") && pair.Value.Contains("折弯"));
		values = ProcessRecognitionService.BuildManualProcessGroupValues("激光下料-手工工序", "钣金件", config);
		Assert.Equal("钣金件", values.Single(pair => pair.Key == config.ProcessTypePropertyName).Value);
		Assert.Equal("激光下料-手工工序", values.Single(pair => pair.Key == config.SummaryPropertyName).Value);
	}

	[Fact]
	public void EditingAnAppliedProcessGroupKeepsItsNameWhenSelectorReturnsToPerRowMode()
	{
		var config = new ProcessRecognitionConfig { WriteCraftDescription = true, Separator = "-" };
		var staged = ProcessRecognitionService.BuildManualProcessGroupValues("激光下料-折弯", "钣金件", config)
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

		Dictionary<string, string> edited = ProcessRecognitionService.MergeManualProcessGroupValues(
			staged, "激光下料-折弯-喷粉", "", config);

		Assert.Equal("钣金件", edited[config.ProcessTypePropertyName]);
		Assert.Equal("激光下料-折弯-喷粉", edited[config.SummaryPropertyName]);
		Assert.Contains("喷粉", edited[config.CraftDescriptionPropertyName]);
	}

	[Fact]
	public void ManualProcessGroupRejectsConflictingConfiguredPropertyNames()
	{
		var config = new ProcessRecognitionConfig { ProcessTypePropertyName = "工序", SummaryPropertyName = "工序" };
		Assert.Throws<InvalidOperationException>(() => ProcessRecognitionService.BuildManualProcessGroupValues("激光下料-折弯", "钣金件", config));
	}

	[Fact]
	public void FlatSheetAndBendContainersDoNotInventBending()
	{
		var input = Input(("基体-法兰1", "SMBaseFlange", false), ("平板型式1", "FlatPattern", false), ("加工-折弯1", "ProcessBends", false), ("展开-折弯1", "FlattenBends", false));
		var result = ProcessRecognitionService.Recognize(input, true);
		Assert.Contains(result, item => item.Name == "激光下料");
		Assert.DoesNotContain(result, item => item.Name == "折弯");
	}
	[Fact]
	public void SuppressedMachiningFeaturesDoNotContributeProcesses()
	{
		var input = Input(("基体法兰1", "SMBaseFlange", false), ("钻孔1", "HoleWzd", false), ("螺纹1", "Thread", true), ("边线法兰1", "EdgeFlange", false));
		foreach (var feature in input.Features.Skip(1)) feature.IsSuppressed = true;
		var result = ProcessRecognitionService.Recognize(input, true);
		Assert.DoesNotContain(result, item => new[] { "钻孔", "攻丝", "折弯" }.Contains(item.Name));
	}
	[Fact]
	public void GeometricBendStillWorksWithoutActiveBendFeature()
	{
		var input = Input(("基体法兰1", "SMBaseFlange", false), ("平板型式1", "FlatPattern", false));
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2, BendCount = 1 };
		Assert.Contains(ProcessRecognitionService.Recognize(input, true), item => item.Name == "折弯");
	}
	[Fact]
	public void SheetMetalFeaturesProduceOrderedManufacturingProcesses()
	{
		ProcessRecognitionInput input = Input(
			("基体法兰1", "SMBaseFlange", false),
			("异型孔向导1", "HoleWzd", false),
			("螺纹线1", "CosmeticThread", false),
			("边线法兰1", "EdgeFlange", false));

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: false);

		Assert.Equal(new[] { "激光下料", "去毛刺", "钻孔", "螺纹加工", "折弯", "检验", "表面处理", "包装", "出货" }, result.Select(item => item.Name));
		Assert.All(result.Where(item => new[] { "激光下料", "钻孔", "螺纹加工", "折弯" }.Contains(item.Name)), item => Assert.True(item.Confidence >= 90));
	}

	[Fact]
	public void SolidMachiningFeaturesDoNotClaimSheetMetalCutting()
	{
		ProcessRecognitionInput input = Input(
			("旋转凸台1", "Revolution", false),
			("拉伸切除1", "Cut", false),
			("孔向导1", "HoleWzd", false));

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: false);

		Assert.Equal(new[] { "车削", "铣削", "钻孔" }, result.Select(item => item.Name));
		Assert.DoesNotContain(result, item => item.Name == "激光下料");
	}

	[Fact]
	public void ImportedBodyGeometryProducesCompleteSheetMetalProcessWithoutFeatureTree()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.Geometry = new ProcessGeometryFact
		{
			SolidBodyCount = 1,
			SheetBodyCount = 1,
			FaceCount = 32,
			PlanarFaceCount = 18,
			CylindricalFaceCount = 14,
			ThicknessMm = 2.5,
			BendCount = 1,
			CutoutCount = 1
		};

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: true);

		Assert.Equal(new[] { "激光下料", "去毛刺", "折弯", "检验", "表面处理", "包装", "出货" }, result.Select(item => item.Name));
		Assert.Contains(result, item => item.Name == "激光下料" && item.Evidence.Contains("无需依赖特征树"));
		Assert.Contains(result, item => item.Name == "折弯" && item.Evidence.Contains("1 组同轴内外圆柱折弯面"));
	}

	[Fact]
	public void GeometryAnalyzerFindsThicknessAndBendFromImportedFaces()
	{
		ProcessPlaneGeometryFact[] planes =
		{
			Plane(1314, new[] { 1d, 0, 0 }, new[] { 30.5, 0, 0 }, 1),
			Plane(1314, new[] { 1d, 0, 0 }, new[] { 28d, 0, 0 }, 1),
			Plane(1107, new[] { 0d, 1, 0 }, new[] { 0d, 2.5, 0 }, 1),
			Plane(1107, new[] { 0d, 1, 0 }, new[] { 0d, 0, 0 }, 1),
			Plane(80, new[] { 0d, 0, 1 }, new[] { 0d, 0, 10 }, 0)
		};
		ProcessCylinderGeometryFact[] cylinders =
		{
			Cylinder(3, 20, Math.PI / 2, new[] { 28d, -1, 0 }, new[] { 0d, 0, 1 }),
			Cylinder(0.5, 20, Math.PI / 2, new[] { 28d, -1, 0 }, new[] { 0d, 0, 1 })
		};

		ProcessGeometryFact result = ProcessGeometryAnalyzer.AnalyzeBody(
			new[] { -30.5, -32.5, -10d, 30.5, 2.5, 44.5 }, planes, cylinders, 32);

		Assert.True(result.IsSheetLike);
		Assert.Equal(2.5, result.ThicknessMm, 3);
		Assert.Equal(1, result.BendCount);
		Assert.Equal(0, result.CutoutCount);
		Assert.False(result.CutoutCountReliable); // Face totals alone do not establish shared hole-wall topology.
	}

	[Fact]
	public void GeometryOnlyConcentricInsertProducesPressRivetProcess()
	{
		ProcessPlaneGeometryFact[] planes =
		{
			Plane(5000, new[] { 0d, 0, 1d }, new[] { 0d, 0, 0d }, 0),
			Plane(5000, new[] { 0d, 0, 1d }, new[] { 0d, 0, 2.5d }, 0),
			Plane(1000, new[] { 1d, 0, 0 }, new[] { 0d, 0, 0d }, 0),
			Plane(1000, new[] { 1d, 0, 0 }, new[] { 40d, 0, 0d }, 0),
			PlaneWithCircles(1000, new[] { 0d, 0, 1 }, new[] { 0d, 0, 0d }, 2, 2, 4),
			Plane(1000, new[] { 0d, 0, 1 }, new[] { 0d, 0, 40d }, 0)
		};
		ProcessCylinderGeometryFact[] cylinders =
		{
			Cylinder(2, 10, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 }),
			Cylinder(4, 9, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 })
		};
		cylinders[0].IsInward = true;

		ProcessGeometryFact geometry = ProcessGeometryAnalyzer.AnalyzeBody(
			new[] { 0d, 0, 0, 40, 40, 40 }, planes, cylinders, 8);
		Assert.Equal(1, geometry.PressFitGeometryCount);
		Assert.Contains("同轴内孔", geometry.FastenerGeometryEvidence);

		ProcessRecognitionInput input = Input(("导入实体", "Imported", false));
		input.Geometry = geometry;
		RecognizedProcess process = ProcessRecognitionService.Recognize(input, true).Single(item => item.Name == "压铆");
		Assert.InRange(process.Confidence, 60, 80);
		Assert.Contains("连接方式待确认", process.Evidence);
	}

	[Fact]
	public void StandaloneConcentricBodyDoesNotClaimPressRivetInstallation()
	{
		ProcessPlaneGeometryFact[] planes =
		{
			Plane(100, new[] { 1d, 0, 0 }, new[] { 0d, 0, 0d }, 0),
			Plane(100, new[] { 1d, 0, 0 }, new[] { 20d, 0, 0d }, 0),
			Plane(100, new[] { 0d, 1, 0 }, new[] { 0d, 20d, 0d }, 0),
			Plane(100, new[] { 0d, 1, 0 }, new[] { 0d, -20d, 0d }, 0)
		};
		ProcessCylinderGeometryFact[] cylinders =
		{
			Cylinder(2, 10, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 }),
			Cylinder(4, 9, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 })
		};
		cylinders[0].IsInward = true;

		ProcessGeometryFact geometry = ProcessGeometryAnalyzer.AnalyzeBody(
			new[] { -20d, -20, 0, 20, 20, 20 }, planes, cylinders, 6);

		Assert.False(geometry.IsSheetLike);
		Assert.Equal(0, geometry.PressFitGeometryCount);
	}

	[Fact]
	public void SideWallConcentricHoleDoesNotClaimPressRivetInstallation()
	{
		var side = PlaneWithCircles(1000, new[] { 1d, 0, 0 }, new[] { 0d, 0, 0d }, 2, 2, 4);
		foreach (ProcessCircleGeometryFact circle in side.InnerCircles) circle.Axis = new[] { 1d, 0, 0 };
		ProcessPlaneGeometryFact[] planes =
		{
			Plane(5000, new[] { 0d, 0, 1d }, new[] { 0d, 0, 0d }, 0),
			Plane(5000, new[] { 0d, 0, 1d }, new[] { 0d, 0, 2.5d }, 0),
			Plane(1000, new[] { 1d, 0, 0 }, new[] { 0d, 0, 0d }, 2), side
		};
		ProcessCylinderGeometryFact[] cylinders =
		{
			Cylinder(2, 2.5, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 1d, 0, 0 }),
			Cylinder(4, 2.5, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 1d, 0, 0 })
		};
		cylinders[0].IsInward = true;
		ProcessGeometryFact geometry = ProcessGeometryAnalyzer.AnalyzeBody(
			new[] { 0d, 0, 0, 40, 40, 40 }, planes, cylinders, 6);

		Assert.Equal(0, geometry.PressFitGeometryCount);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void StraightRectangularTubeDoesNotTurnSectionCornersIntoBending(bool rounded)
	{
		var (planes, cylinders) = StraightTubeFaces(rounded);
		var geometry = ProcessGeometryAnalyzer.AnalyzeBody(new[] { -20d, -10, -100, 20, 10, 100 }, planes, cylinders, planes.Length + cylinders.Length);
		var input = Input(("输入1", "Imported", false));
		input.Geometry = ProcessGeometryAnalyzer.Merge(new[] { geometry });
		var processes = ProcessRecognitionService.Recognize(input, true);

		Assert.Equal(1, input.Geometry.StraightTubeBodyCount);
		Assert.Equal(2, input.Geometry.ThicknessMm, 3);
		Assert.Equal(0, input.Geometry.BendCount);
		Assert.False(input.Geometry.IsSheetLike);
		Assert.Contains(processes, process => process.Name == "锯切下料");
		Assert.DoesNotContain(processes, process => process.Name == "折弯" || process.Name == "激光下料");
	}

	[Fact]
	public void TubeWithChangingSideAxisIsNotClassifiedAsStraight()
	{
		var (planes, cylinders) = StraightTubeFaces(true);
		foreach (var cylinder in cylinders.Take(2)) cylinder.Axis = new[] { 1d, 0, 0 };
		var geometry = ProcessGeometryAnalyzer.AnalyzeBody(new[] { -20d, -10, -100, 20, 10, 100 }, planes, cylinders, planes.Length + cylinders.Length);
		var input = Input(("输入1", "Imported", false));
		input.ProcessType = "方通";
		input.Geometry = geometry;

		Assert.Equal(0, geometry.StraightTubeBodyCount);
		Assert.Contains(ProcessRecognitionService.Recognize(input, true), process => process.Name == "折弯");
	}

	[Theory]
	[InlineData("Boss")]
	[InlineData("BossThin")]
	[InlineData("Extrusion")]
	[InlineData("ExtrudeThin")]
	[InlineData("Extrude")]
	public void CertifiedStraightTubeExtrusionDoesNotInventMilling(string featureType)
	{
		var (planes, cylinders) = StraightTubeFaces(true);
		var input = Input(("拉伸凸台1", featureType, false));
		input.Geometry = ProcessGeometryAnalyzer.AnalyzeBody(new[] { -20d, -10, -100, 20, 10, 100 }, planes, cylinders, planes.Length + cylinders.Length);
		var result = ProcessRecognitionService.Recognize(input, true);
		Assert.Contains(result, process => process.Name == "锯切下料");
		Assert.DoesNotContain(result, process => process.Name == "铣削" || process.Name == "折弯");
	}

	[Theory]
	[InlineData("切除1", "Cut")]
	[InlineData("切除1", "CutThin")]
	[InlineData("切除1", "ExtrusionCut")]
	[InlineData("铣削1", "Boss")]
	public void StraightTubeRetainsCutAndExplicitMillingEvidence(string name, string featureType)
	{
		var input = Input(("拉伸凸台1", "Boss", false), (name, featureType, false));
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, StraightTubeBodyCount = 1, ThicknessMm = 2 };
		Assert.Contains(ProcessRecognitionService.Recognize(input, true), process => process.Name == "铣削");
	}

	[Fact]
	public void UncertifiedBodyAlongsideTubeRetainsExtrusionMachiningInference()
	{
		var input = Input(("拉伸凸台1", "Boss", false));
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 2, StraightTubeBodyCount = 1, ThicknessMm = 2 };
		Assert.Contains(ProcessRecognitionService.Recognize(input, true), process => process.Name == "铣削");
	}

	[Theory]
	[InlineData("方管")]
	[InlineData("方通")]
	[InlineData("矩形管")]
	[InlineData("扁通")]
	public void TubeAliasesPreserveExplicitBendingFeatures(string processType)
	{
		var input = Input(("折弯1", "SketchBend", false));
		input.ProcessType = processType;
		var result = ProcessRecognitionService.Recognize(input, true);
		Assert.Contains(result, process => process.Name == "锯切下料");
		Assert.Contains(result, process => process.Name == "折弯");
	}

	[Fact]
	public void IncompleteGeometryCannotCertifyStraightTubeOrHideBending()
	{
		var (planes, cylinders) = StraightTubeFaces(true);
		var geometry = ProcessGeometryAnalyzer.AnalyzeBody(new[] { -20d, -10, -100, 20, 10, 100 }, planes, cylinders, planes.Length + cylinders.Length + 1);
		Assert.Equal(0, geometry.StraightTubeBodyCount);
		Assert.Equal(4, geometry.BendCount);
	}

	[Fact]
	public void MixedTubeAndBentSheetRetainsSheetBending()
	{
		var (planes, cylinders) = StraightTubeFaces(true);
		var tube = ProcessGeometryAnalyzer.AnalyzeBody(new[] { -20d, -10, -100, 20, 10, 100 }, planes, cylinders, planes.Length + cylinders.Length);
		var merged = ProcessGeometryAnalyzer.Merge(new[] { tube,
			new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 3, BendCount = 1 } });
		Assert.Equal(1, merged.BendCount);
		Assert.Equal(1, merged.SheetBodyCount);
		Assert.Equal(1, merged.StraightTubeBodyCount);
		Assert.Equal(3, merged.ThicknessMm);
	}

	private static (ProcessPlaneGeometryFact[] Planes, ProcessCylinderGeometryFact[] Cylinders) StraightTubeFaces(bool rounded)
	{
		var planes = new List<ProcessPlaneGeometryFact>();
		foreach (double x in new[] { -20d, -18, 18, 20 }) planes.Add(Plane(2400, new[] { 1d, 0, 0 }, new[] { x, 0, 0 }, 0));
		foreach (double y in new[] { -10d, -8, 8, 10 }) planes.Add(Plane(6400, new[] { 0d, 1, 0 }, new[] { 0d, y, 0 }, 0));
		foreach (double z in new[] { -100d, 100 }) planes.Add(Plane(214, new[] { 0d, 0, 1 }, new[] { 0d, 0, z }, 1));
		var cylinders = new List<ProcessCylinderGeometryFact>();
		if (rounded)
			foreach (double x in new[] { -16d, 16 })
				foreach (double y in new[] { -6d, 6 })
					foreach (double radius in new[] { 2d, 4 })
						cylinders.Add(Cylinder(radius, 200, Math.PI / 2, new[] { x, y, 0 }, new[] { 0d, 0, 1 }));
		return (planes.ToArray(), cylinders.ToArray());
	}

	[Fact]
	public void DeepRecognitionIncludesNamedSubFeatureProcesses()
	{
		ProcessRecognitionInput input = Input(
			("拉伸凸台1", "Boss", false),
			("喷粉", "ProfileFeature", true));

		Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, deep: false), item => item.Name == "喷粉");
		Assert.Contains(ProcessRecognitionService.Recognize(input, deep: true), item => item.Name == "喷粉" && item.Confidence == 96);
	}

	[Theory]
	[InlineData("水刀切割", "水切下料")]
	[InlineData("等离子切割", "等离子下料")]
	[InlineData("火焰切割", "火焰下料")]
	[InlineData("型材锯", "锯切下料")]
	public void NamedFeatureManufacturingHintsRecognizeAdditionalCuttingProcesses(string featureName, string expectedProcess)
	{
		ProcessRecognitionInput input = Input((featureName, "ProfileFeature", false));

		Assert.Contains(ProcessRecognitionService.Recognize(input, deep: false), process => process.Name == expectedProcess);
	}

	[Fact]
	public void DuplicateEvidenceCreatesOneProcessAtHighestConfidence()
	{
		ProcessRecognitionInput input = Input(
			("焊件", "Weldment", false),
			("焊缝1", "WeldBead", false));

		RecognizedProcess welding = Assert.Single(ProcessRecognitionService.Recognize(input, deep: false), item => item.Name == "焊接");

		Assert.Equal(97, welding.Confidence);
		Assert.Contains("焊缝", welding.Evidence);
	}

	[Fact]
	public void WeldmentCutListDoesNotBecomeMilling()
	{
		ProcessRecognitionInput input = Input(
			("焊件", "Weldment", false),
			("切割清单", "CutListFolder", false));

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: false);

		Assert.Contains(result, item => item.Name == "锯切下料");
		Assert.DoesNotContain(result, item => item.Name == "铣削");
	}

	[Fact]
	public void UnknownFeaturesRemainEditableInsteadOfReportingFalseSuccess()
	{
		ProcessRecognitionInput input = Input(("基准面1", "RefPlane", false));

		RecognizedProcess result = Assert.Single(ProcessRecognitionService.Recognize(input, deep: false));

		Assert.Equal("未识别", result.Name);
		Assert.Equal(0, result.Confidence);
	}

	[Fact]
	public void PropertyTextDropsUnknownAndDuplicateRows()
	{
		string value = ProcessRecognitionService.Join(new[] { "激光下料", "", "折弯", "未识别", "未知", "折弯" }, "-");

		Assert.Equal("激光下料-折弯", value);
	}

	[Fact]
	public void PropertyTextKeepsAllProcessesWithoutStepLimit()
	{
		string[] processes = Enumerable.Range(1, 30).Select(index => "工序" + index).ToArray();

		string value = ProcessRecognitionService.Join(processes, "-");

		Assert.Equal(string.Join("-", processes), value);
		Assert.Equal(30, value.Split('-').Length);
	}

	[Fact]
	public void GeometryValuesAreWrittenWithUnlimitedProcessSummary()
	{
		ProcessRecognitionConfig config = new ProcessRecognitionConfig();
		RecognizedProcess[] processes = Enumerable.Range(1, 30)
			.Select(index => new RecognizedProcess { Name = "工序" + index, Confidence = 80, Evidence = "测试" })
			.ToArray();
		ProcessGeometryFact geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2.5, BendCount = 4, CutoutCount = 7 };

		IReadOnlyList<KeyValuePair<string, string>> values = ProcessRecognitionService.BuildPropertyValues(processes, geometry, config);

		Assert.Equal(30, values.Single(value => value.Key == "工序").Value.Split('-').Length);
		Assert.Equal("2.5", values.Single(value => value.Key == "板厚").Value);
		Assert.Equal("4", values.Single(value => value.Key == "折弯下刀数").Value);
		Assert.Equal("7", values.Single(value => value.Key == "内轮廓数").Value);
	}

	[Fact]
	public void StandardFastenerUsesPurchasedPartFlowInsteadOfMachining()
	{
		ProcessRecognitionInput input = Input(("旋转凸台1", "Revolution", false), ("螺纹1", "Thread", false));
		input.DocumentTitle = "GB5783-六角头螺栓.SLDPRT";

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "外购", "来料检验", "入库" }, result);
		Assert.DoesNotContain("车削", result);
	}

	[Theory]
	[InlineData("是", true)]
	[InlineData("采购", true)]
	[InlineData("自制", false)]
	[InlineData("非外购", false)]
	public void PurchaseFlagRecognizesCommonPositiveAndNegativeValues(string flag, bool expectedPurchased)
	{
		ProcessRecognitionInput input = Input(("ImportedFeature", "Imported", false));
		input.DocumentTitle = "未知部件.SLDPRT";
		input.Purchase = flag;

		bool purchased = ProcessRecognitionService.Recognize(input, deep: false).Any(process => process.Name == "外购");

		Assert.Equal(expectedPurchased, purchased);
	}

	[Fact]
	public void ExplicitStandardPartTypeOverridesGeometry()
	{
		ProcessRecognitionInput input = Input(("拉伸凸台1", "Boss", false));
		input.PartType = "标准件";
		input.DocumentTitle = "自定义件.SLDPRT";

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "外购", "来料检验", "入库" }, result);
	}

	[Fact]
	public void OutsourceSurfacePropertyOverridesGenericSurfaceFallback()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.Outsource = "喷粉";
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 1.5 };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Contains("喷粉", result);
		Assert.DoesNotContain("表面处理", result);
	}

	[Fact]
	public void NonSheetImportedSolidGetsReviewableMachiningFallback()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, FaceCount = 12, CylindricalFaceCount = 4 };

		RecognizedProcess result = Assert.Single(ProcessRecognitionService.Recognize(input, deep: false));

		Assert.Equal("机加工", result.Name);
		Assert.Equal(55, result.Confidence);
	}

	[Fact]
	public void GroupWeldAssemblyUsesWeldFlowBeforeGenericAssembly()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput
		{
			IsAssembly = true,
			DocumentTitle = "柜体组焊总成.SLDASM",
			PartType = "组焊件"
		};

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "组焊", "打磨", "检验" }, result);
	}

	[Fact]
	public void ProcessTypeWeldingCornerAddsDeclaredWeldingAndGrinding()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.ProcessType = "焊接封角-打磨";
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1 };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Contains("焊接封角", result);
		Assert.Contains("打磨", result);
		Assert.Contains("检验", result);
	}

	[Fact]
	public void ProcessTypeSquareTubeOverridesSheetGuessWithSawCutFlow()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.ProcessType = "方管";
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, SheetBodyCount = 1, ThicknessMm = 2 };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Contains("锯切下料", result);
		Assert.Contains("去毛刺", result);
		Assert.Contains("检验", result);
		Assert.DoesNotContain("激光下料", result);
	}

	[Fact]
	public void AssemblyProcessTypeWeldingUsesGroupWeldFlow()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput { IsAssembly = true, DocumentTitle = "柜体总成.SLDASM", ProcessType = "焊接" };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "组焊", "打磨", "检验" }, result);
	}

	[Fact]
	public void OrdinaryAssemblyUsesLightweightAssemblyFlow()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput { IsAssembly = true, DocumentTitle = "门板组件.SLDASM" };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "装配", "检验" }, result);
	}

	[Fact]
	public void AisiMaterialUsesConfiguredSurfaceProcessInsteadOfUnknown()
	{
		ProcessRecognitionInput input = Input(("基体-法兰1", "SMBaseFlange", false), ("平板型式1", "FlatPattern", false));
		input.Material = "AISI 304";

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: true, new ProcessRecognitionConfig());

		Assert.Contains(result, process => process.Name == "拉丝" && process.Evidence.Contains("AISI=拉丝"));
		Assert.DoesNotContain(result, process => process.Name == "未知");
	}

	[Fact]
	public void ExplicitSurfacePropertyOverridesMaterialMapping()
	{
		ProcessRecognitionInput input = Input(("基体-法兰1", "SMBaseFlange", false));
		input.Material = "AISI 304";
		input.SurfaceTreatment = "镜面抛光";

		IReadOnlyList<RecognizedProcess> result = ProcessRecognitionService.Recognize(input, deep: true, new ProcessRecognitionConfig());

		Assert.Contains(result, process => process.Name == "镜面抛光" && process.Confidence == 99);
		Assert.DoesNotContain(result, process => process.Name == "拉丝");
	}

	[Fact]
	public void ExplicitSurfacePropertyIsLoadedForNonSheetPart()
	{
		ProcessRecognitionInput input = Input(("输入1", "Imported", false));
		input.SurfaceTreatment = "镜面抛光";
		input.Geometry = new ProcessGeometryFact { SolidBodyCount = 1, FaceCount = 12 };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false, new ProcessRecognitionConfig())
			.Select(process => process.Name)
			.ToArray();

		Assert.Equal(new[] { "机加工", "镜面抛光" }, result);
	}

	[Fact]
	public void ExplicitSurfacePropertyIsLoadedForStandardPart()
	{
		ProcessRecognitionInput input = Input(("拉伸凸台1", "Boss", false));
		input.PartType = "标准件";
		input.SurfaceTreatment = "镀锌";

		string[] result = ProcessRecognitionService.Recognize(input, deep: false, new ProcessRecognitionConfig())
			.Select(process => process.Name)
			.ToArray();

		Assert.Equal(new[] { "外购", "镀锌", "来料检验", "入库" }, result);
	}

	[Fact]
	public void CompositeSurfacePropertyBecomesIndependentOrderedProcesses()
	{
		ProcessRecognitionInput input = Input(("基体-法兰1", "SMBaseFlange", false));
		input.SurfaceTreatment = "清洗+烤漆/丝印";

		string[] result = ProcessRecognitionService.Recognize(input, deep: true, new ProcessRecognitionConfig())
			.Select(process => process.Name)
			.ToArray();

		Assert.Contains("清洗", result);
		Assert.Contains("烤漆", result);
		Assert.Contains("丝印", result);
		Assert.True(Array.IndexOf(result, "清洗") < Array.IndexOf(result, "烤漆"));
		Assert.True(Array.IndexOf(result, "烤漆") < Array.IndexOf(result, "丝印"));
		Assert.DoesNotContain("清洗+烤漆/丝印", result);
	}

	[Fact]
	public void NamedFormingAndEquipmentHintsKeepSpecificProcessNames()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput { IsPart = true, Features = new[]
		{
			new ProcessFeatureFact { Name = "凸包成形", TypeName = "FormToolInstance" },
			new ProcessFeatureFact { Name = "龙门铣加工", TypeName = "Cut" },
			new ProcessFeatureFact { Name = "总装", TypeName = "Note" }
		} };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false, new ProcessRecognitionConfig())
			.Select(process => process.Name)
			.ToArray();

		Assert.Contains("打凸包", result);
		Assert.DoesNotContain("冲压成形", result);
		Assert.Contains("龙门铣", result);
		Assert.Contains("装配", result);
	}

	[Fact]
	public void NativeFeatureTypesProvideFormingEvidenceWithoutLocalizedNames()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput { IsPart = true, Features = new[]
		{
			new ProcessFeatureFact { Name = "FormToolInstance1", TypeName = "LouverFeature" },
			new ProcessFeatureFact { Name = "FormToolInstance2", TypeName = "DomeFeature" }
		} };

		string[] result = ProcessRecognitionService.Recognize(input, deep: false, new ProcessRecognitionConfig())
			.Select(process => process.Name)
			.ToArray();

		Assert.Contains("冲百叶窗", result);
		Assert.Contains("打凸包", result);
		Assert.DoesNotContain("冲压成形", result);
	}

	[Fact]
	public void SurfaceFallbackAndInspectionOrderFollowSettings()
	{
		ProcessRecognitionInput input = Input(("基体-法兰1", "SMBaseFlange", false));
		input.Material = "AL6061";
		ProcessRecognitionConfig config = new ProcessRecognitionConfig { InspectionAfterSurface = true };

		string[] result = ProcessRecognitionService.Recognize(input, deep: true, config).Select(process => process.Name).ToArray();

		Assert.Contains("表面处理", result);
		Assert.True(Array.IndexOf(result, "表面处理") < Array.IndexOf(result, "检验"));
	}

	[Fact]
	public void ConfiguredProcessNamesMappingsAndAdditionalProcessesAreApplied()
	{
		ProcessRecognitionInput input = Input(("基体-法兰1", "SMBaseFlange", false));
		ProcessRecognitionConfig config = new ProcessRecognitionConfig
		{
			DeburrProcessName = "打磨毛刺",
			InspectionProcessName = "终检",
			PackagingProcessName = "木箱包装",
			ShippingProcessName = "待发货",
			AdditionalProcesses = "贴标签|拍照归档",
			ProcessNameMappings = "激光下料=激光切割"
		};

		string[] result = ProcessRecognitionService.Recognize(input, deep: true, config).Select(process => process.Name).ToArray();

		Assert.Contains("激光切割", result);
		Assert.Contains("打磨毛刺", result);
		Assert.Contains("终检", result);
		Assert.Contains("木箱包装", result);
		Assert.Contains("待发货", result);
		Assert.Contains("贴标签", result);
		Assert.Contains("拍照归档", result);
	}

	[Fact]
	public void CraftDescriptionKeepsProcessEvidence()
	{
		string value = ProcessRecognitionService.BuildCraftDescription(new[]
		{
			new RecognizedProcess { Name = "激光下料", Confidence = 94, Evidence = "检测到钣金基体" },
			new RecognizedProcess { Name = "未知", Confidence = 0, Evidence = "无" }
		});

		Assert.Contains("激光下料（94%，检测到钣金基体）", value);
		Assert.DoesNotContain("未知", value);
	}

	private static ProcessRecognitionInput Input(params (string Name, string TypeName, bool IsSubFeature)[] features)
	{
		return new ProcessRecognitionInput
		{
			IsPart = true,
			Features = features.Select(feature => new ProcessFeatureFact
			{
				Name = feature.Name,
				TypeName = feature.TypeName,
				IsSubFeature = feature.IsSubFeature
			}).ToList()
		};
	}

	private static ProcessPlaneGeometryFact Plane(double area, double[] normal, double[] point, int innerLoops)
	{
		return new ProcessPlaneGeometryFact { AreaMm2 = area, Normal = normal, PointMm = point, InnerLoopCount = innerLoops };
	}

	private static ProcessPlaneGeometryFact PlaneWithCircles(double area, double[] normal, double[] point, int innerLoops, params double[] radii)
	{
		return new ProcessPlaneGeometryFact
		{
			AreaMm2 = area,
			Normal = normal,
			PointMm = point,
			InnerLoopCount = innerLoops,
			InnerCircles = radii.Select(radius => new ProcessCircleGeometryFact { RadiusMm = radius, CenterMm = new[] { 0d, 0, 0 }, Axis = new[] { 0d, 0, 1d } }).ToArray()
		};
	}

	private static ProcessCylinderGeometryFact Cylinder(double radius, double length, double angle, double[] point, double[] axis)
	{
		return new ProcessCylinderGeometryFact
		{
			RadiusMm = radius,
			AxisLengthMm = length,
			AxisCenterMm = 0,
			AreaMm2 = radius * length * angle,
			AxisPointMm = point,
			Axis = axis
		};
	}
}
