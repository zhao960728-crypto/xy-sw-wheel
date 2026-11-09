using System.Collections.Generic;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class ProcessPartTypeTests
{
	private static ProcessRecognitionInput Input(string partType = "", string title = "") => new ProcessRecognitionInput
	{
		PartType = partType,
		DocumentTitle = title,
		IsPart = true
	};

	[Fact]
	public void CleanModelCanBeReadInItsReferencedConfiguration()
	{
		Assert.True(ProcessConfigurationReadPolicy.CanReadTarget("Default", "外购件", false, false, out string reason));
		Assert.Equal("", reason);
	}

	[Fact]
	public void DirtyOpenModelIsNotSwitchedAwayFromItsActiveConfiguration()
	{
		Assert.False(ProcessConfigurationReadPolicy.CanReadTarget("Default", "外购件", true, false, out string reason));
		Assert.Contains("未保存更改", reason);
		Assert.True(ProcessConfigurationReadPolicy.CanReadTarget("Default", "Default", true, false, out reason));
	}

	[Theory]
	[InlineData("脚垫", PartTypes.FootPad)]
	[InlineData("万向调整脚", PartTypes.FootPad)]
	[InlineData("重型地脚", PartTypes.FootPad)]
	[InlineData("方通", PartTypes.Profile)]
	[InlineData("矩形管", PartTypes.Profile)]
	[InlineData("Q235 矩形钢管 40x40x2", PartTypes.Profile)]
	[InlineData("RHS 40x40x2", PartTypes.Profile)]
	[InlineData("SHS 40x40x2", PartTypes.Profile)]
	[InlineData("角钢", PartTypes.Profile)]
	[InlineData("槽钢", PartTypes.Profile)]
	[InlineData("H型钢", PartTypes.Profile)]
	[InlineData("C型钢", PartTypes.Profile)]
	[InlineData("冷弯型钢", PartTypes.Profile)]
	[InlineData("组焊件", PartTypes.WeldAssembly)]
	[InlineData("焊接机架", PartTypes.WeldAssembly)]
	[InlineData("外购件", PartTypes.Purchased)]
	[InlineData("伺服电机", PartTypes.Purchased)]
	[InlineData("直线导轨", PartTypes.Purchased)]
	[InlineData("滚珠丝杆模组", PartTypes.Purchased)]
	[InlineData("安全光栅", PartTypes.Purchased)]
	[InlineData("真空发生器", PartTypes.Purchased)]
	[InlineData("标准件", PartTypes.Standard)]
	[InlineData("外六角螺栓", PartTypes.Standard)]
	[InlineData("压铆螺母", PartTypes.Standard)]
	[InlineData("外加工件", PartTypes.Outsourced)]
	[InlineData("铝压铸壳体", PartTypes.Casting)]
	[InlineData("热锻法兰", PartTypes.Forging)]
	[InlineData("注塑透明罩", PartTypes.Plastic)]
	[InlineData("FDM打印夹具", PartTypes.Additive)]
	public void FileNameClassifiesPartType(string title, string expected)
	{
		PartTypeMatch match = PartTypeCatalog.FromText("", title);
		Assert.Equal(expected, match.Type);
		Assert.False(match.IsEmpty);
		Assert.Contains("文件名", match.Evidence);
	}

	[Fact]
	public void DeclaredPropertyWinsOverFileName()
	{
		PartTypeMatch match = PartTypeCatalog.FromText("组焊件", "脚垫");
		Assert.Equal(PartTypes.WeldAssembly, match.Type);
		Assert.Contains("自定义属性", match.Evidence);
	}

	[Fact]
	public void GenericOtherTypeDoesNotHidePurchasedFilename()
	{
		ProcessRecognitionInput input = Input("其他", "伺服电机-750W.SLDPRT");

		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, deep: false);

		Assert.Equal(new[] { "外购", "来料检验", "入库" }, processes.Select(process => process.Name));
		Assert.Equal(PartTypes.Purchased, ProcessRecognitionService.ResolvePartType(input, false, false, false, false, false, false, false).Type);
	}

	[Fact]
	public void LongestTypeKeywordPreventsSlidingBlockSeatFromBeingMarkedPurchased()
	{
		PartTypeMatch match = PartTypeCatalog.FromText("", "滑块座.sldprt");

		Assert.Equal(PartTypes.Machined, match.Type);
	}

	[Theory]
	[InlineData("其他", "零件-40x40.sldprt", "RHS 40x40x2")]
	[InlineData("", "零件-40x40.sldprt", "矩形空心管 40x40x2")]
	public void ProfileTypeNameOrProfileDescriptionAddsSawCutChain(string partType, string title, string profileDescription)
	{
		ProcessRecognitionInput input = Input(partType, title);
		input.ProfileDescription = profileDescription;

		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, deep: false);

		Assert.Equal(new[] { "锯切下料", "去毛刺", "检验", "包装" }, processes.Select(process => process.Name));
		Assert.Equal(PartTypes.Profile, ProcessRecognitionService.ResolvePartType(input, false, false, false, false, false, false, false).Type);
	}

	[Theory]
	[InlineData("角钢")]
	[InlineData("槽钢")]
	[InlineData("H型钢")]
	[InlineData("C型钢")]
	public void StructuralProfileProcessTypeAddsSawCutDeburrInspectionAndPackaging(string processType)
	{
		ProcessRecognitionInput input = Input("", "自定义型材.sldprt");
		input.ProcessType = processType;

		string[] processes = ProcessRecognitionService.Recognize(input, deep: false).Select(process => process.Name).ToArray();

		Assert.Equal(new[] { "锯切下料", "去毛刺", "检验", "包装" }, processes);
	}

	[Fact]
	public void AssemblyRecognitionTableShowsTheResolvedStructuralProfileType()
	{
		ProcessRecognitionInput input = Input("", "40x40角钢.SLDPRT");
		string classification = AssemblyProcessRecognitionWindow.Classification(input,
			new[] { new RecognizedProcess { Name = "锯切下料" }, new RecognizedProcess { Name = "去毛刺" } });

		Assert.Equal(PartTypes.Profile, classification);
	}

	[Fact]
	public void AssemblyRecognitionTableKeepsWeldedAssemblyClassification()
	{
		ProcessRecognitionInput input = new ProcessRecognitionInput { IsAssembly = true };

		Assert.Equal("焊接总成", AssemblyProcessRecognitionWindow.Classification(input,
			new[] { new RecognizedProcess { Name = "组焊" } }));
	}

	[Fact]
	public void UnknownDeclaredValueIsKeptAsCustomType()
	{
		PartTypeMatch match = PartTypeCatalog.FromText("临时周转件", "");
		Assert.Equal("临时周转件", match.Type);
		Assert.Contains("自定义类型", match.Evidence);
	}

	[Fact]
	public void DeclaredValueContainingKnownKeywordIsClassified()
	{
		// A declared type that contains a known keyword still resolves to that
		// canonical type instead of being kept verbatim.
		Assert.Equal(PartTypes.SheetMetal, PartTypeCatalog.FromText("钣金门板组件", "").Type);
	}

	[Fact]
	public void GeometryOnlyClassifiesWhenNothingDeclared()
	{
		Assert.Equal(PartTypes.Weldment, PartTypeCatalog.FromGeometry(false, true, false, false, false, false, false).Type);
		Assert.Equal(PartTypes.Profile, PartTypeCatalog.FromGeometry(false, false, false, true, false, false, false).Type);
		Assert.Equal(PartTypes.SheetMetal, PartTypeCatalog.FromGeometry(true, false, false, false, false, false, false).Type);
		Assert.Equal(PartTypes.Mixed, PartTypeCatalog.FromGeometry(true, true, false, false, false, false, false).Type);
		Assert.True(PartTypeCatalog.FromGeometry(false, false, false, false, false, false, false).IsEmpty);
	}

	[Fact]
	public void DefaultChainsCoverEveryClassifiedType()
	{
		foreach (string type in PartTypes.All)
		{
			if (type == PartTypes.Other) continue;
			Assert.NotEmpty(PartTypeCatalog.DefaultChain(type));
		}
		Assert.Contains(PartTypes.FootPad + "=外购>来料检验>入库", PartTypeCatalog.DefaultChains);
		Assert.Contains(PartTypes.WeldAssembly + "=锯切下料>去毛刺>组焊>打磨>检验>包装", PartTypeCatalog.DefaultChains);
	}

	[Fact]
	public void TypeChainConfigOverridesDefault()
	{
		ProcessRecognitionConfig config = new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(config);
		config.PartTypeChains = "组焊件=下料>点焊>打磨";
		IReadOnlyList<string> chain = ProcessRecognitionConfig.ResolveTypeChain(config, PartTypes.WeldAssembly);
		Assert.Equal(new[] { "下料", "点焊", "打磨" }, chain);
	}

	[Fact]
	public void EmptyChainFallsBackToCatalogDefault()
	{
		ProcessRecognitionConfig config = new ProcessRecognitionConfig { PartTypeChains = "" };
		ProcessRecognitionConfig.Normalize(config);
		Assert.Equal(PartTypeCatalog.DefaultChain(PartTypes.Profile),
			ProcessRecognitionConfig.ResolveTypeChain(config, PartTypes.Profile));
	}

	[Fact]
	public void RecognitionFallsBackToTypeChainWithoutFeatures()
	{
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(
			Input(title: "方通"), false);
		Assert.Equal(new[] { "锯切下料", "去毛刺", "检验", "包装" }, processes.Select(p => p.Name).ToArray());
		Assert.Contains("方通", processes[0].Evidence);
	}

	[Fact]
	public void FootPadBecomesPurchasingChain()
	{
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(
			Input(title: "万向脚杯"), false);
		Assert.Equal(new[] { "外购", "来料检验", "入库" }, processes.Select(p => p.Name).ToArray());
	}

	[Theory]
	[InlineData("铸铝壳体", "铸造>清砂/去浇口>热处理>机加工>检验")]
	[InlineData("热锻法兰", "锻造>热处理>机加工>去毛刺>检验")]
	[InlineData("注塑透明罩", "注塑/成型>去毛刺>检验>包装")]
	[InlineData("FDM打印夹具", "3D打印>去支撑>打磨/后处理>检验")]
	public void NewlyClassifiedManufacturingTypesGetReviewableDefaultChains(string title, string expected)
	{
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(Input(title: title), false);
		Assert.Equal(expected.Split('>'), processes.Select(process => process.Name));
	}

	[Fact]
	public void OutsourcedFilenameProducesReturnInspectionAndStockingSteps()
	{
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(Input(title: "外加工定位块"), false);
		Assert.Equal(new[] { "外加工", "回厂检验", "入库" }, processes.Select(process => process.Name));
	}

	[Theory]
	[InlineData("SLM 打印成形", "3D打印")]
	[InlineData("压铸", "铸造")]
	[InlineData("模锻", "锻造")]
	[InlineData("人工时效", "时效处理")]
	[InlineData("去支撑结构", "去支撑")]
	public void FeatureNamesAddSpecificManufacturingOperations(string featureName, string expected)
	{
		ProcessRecognitionInput input = Input();
		input.Features = new List<ProcessFeatureFact> { new ProcessFeatureFact { Name = featureName, TypeName = "Feature" } };
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, false);
		Assert.Contains(expected, processes.Select(process => process.Name));
	}

	[Fact]
	public void StockFormedPartTypeDoesNotTreatItsBaseExtrusionAsMilling()
	{
		ProcessRecognitionInput input = Input("塑胶件", "注塑透明罩");
		input.Features = new List<ProcessFeatureFact> { new ProcessFeatureFact { Name = "Boss-Extrude1", TypeName = "Boss-Extrude" } };
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, false);
		Assert.Equal(new[] { "注塑/成型", "去毛刺", "检验", "包装" }, processes.Select(process => process.Name));
	}

	[Fact]
	public void BatchCacheKeySeparatesReferencedConfigurations()
	{
		string first = ProcessRecognitionBatchCache.KeyForConfiguration(@"C:\Parts\Frame.SLDPRT", "焊接");
		string second = ProcessRecognitionBatchCache.KeyForConfiguration(@"C:\Parts\Frame.SLDPRT", "机加工");
		Assert.NotEqual(first, second);
		Assert.Equal(first, ProcessRecognitionBatchCache.KeyForConfiguration(@"c:\parts\frame.sldprt", "焊接"));
	}

	[Fact]
	public void BatchCacheRestoresOnlyTheMatchingReferencedConfiguration()
	{
		string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xy-process-cache-" + System.Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(directory);
		try
		{
			string model = System.IO.Path.Combine(directory, "Frame.SLDPRT");
			string cacheFile = System.IO.Path.Combine(directory, "cache.json");
			System.IO.File.WriteAllText(model, "fixture");
			ProcessRecognitionConfig config = new ProcessRecognitionConfig();
			ProcessRecognitionBatchCache cache = new ProcessRecognitionBatchCache(config, cacheFile);
			cache.Put(model, "焊接", "型材件", "锯切下料-组焊");
			cache.Save();

			ProcessRecognitionBatchCache restored = new ProcessRecognitionBatchCache(config, cacheFile);
			Assert.True(restored.TryGet(model, "焊接", out string type, out string process));
			Assert.Equal("型材件", type);
			Assert.Equal("锯切下料-组焊", process);
			Assert.False(restored.TryGet(model, "机加工", out _, out _));
		}
		finally
		{
			try { System.IO.Directory.Delete(directory, recursive: true); } catch { }
		}
	}

	[Theory]
	[InlineData("压铆螺母M4", FastenerProcesses.PressRivet)]
	[InlineData("PEM S-M3-1", FastenerProcesses.PressRivet)]
	[InlineData("涨铆螺母", FastenerProcesses.PressRivet)]
	[InlineData("圆柱销压入", FastenerProcesses.PressPin)]
	[InlineData("种焊螺母", FastenerProcesses.WeldRivet)]
	[InlineData("凸焊螺柱", FastenerProcesses.WeldRivet)]
	public void FastenerFeaturesAddTheirProcess(string featureName, string expected)
	{
		ProcessRecognitionInput input = Input();
		input.Features = new List<ProcessFeatureFact>
		{
			new ProcessFeatureFact { Name = featureName, TypeName = "HoleWzd" }
		};
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, false);
		Assert.Contains(expected, processes.Select(p => p.Name));
	}

	[Fact]
	public void FastenerProcessesStayOrderedAfterTapping()
	{
		ProcessRecognitionInput input = Input();
		input.Features = new List<ProcessFeatureFact>
		{
			new ProcessFeatureFact { Name = "M4 螺纹孔", TypeName = "CosmeticThread" },
			new ProcessFeatureFact { Name = "压铆螺母", TypeName = "HoleWzd" }
		};
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, false);
		List<string> names = processes.Select(p => p.Name).ToList();
		Assert.True(names.IndexOf("攻丝") < names.IndexOf(FastenerProcesses.PressRivet));
	}

	[Fact]
	public void UnclassifiedPartStillReportsUnrecognized()
	{
		ProcessRecognitionInput input = Input(title: "未知名零件");
		input.Geometry = new ProcessGeometryFact();
		IReadOnlyList<RecognizedProcess> processes = ProcessRecognitionService.Recognize(input, false);
		Assert.Equal("未识别", processes[0].Name);
	}
}
