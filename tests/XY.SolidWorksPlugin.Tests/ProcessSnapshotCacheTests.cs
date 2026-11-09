using System;
using System.IO;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class ProcessSnapshotCacheTests
{
    [Fact]
    public void SavedResultRestoresFullInputAcrossCacheInstancesWithoutModelRead()
    {
        var folder = Path.Combine(Path.GetTempPath(), "xy-process-snapshot-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "part.sldprt"), cacheFile = Path.Combine(folder, "cache.json"); File.WriteAllText(file, "unchanged model bytes");
            var config = new ProcessRecognitionConfig { Separator = "→" };
            var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = "普通编号", ItemDescription = "型材", StandardCode = "RHS",
                PurchaseMarkerSnapshot = new PropertyWriteBatch.Snapshot { Exists = true, Type = 30, Raw = "否", Resolved = "否" },
                Features = new[] { new ProcessFeatureFact { TypeName = "Cut", Name = "孔", IsSubFeature = true } },
                Geometry = ProcessGeometryAnalyzer.Merge(new[] { new ProcessGeometryFact { BodyName = "框架", ProfileKind = "方通", SolidBodyCount = 1, StraightTubeBodyCount = 1, ThicknessMm = 2 } }) };
            var rows = new[] { new RecognizedProcess { Name = "锯切下料", Confidence = 90, Evidence = "真实几何" }, new RecognizedProcess { Name = "检验", Confidence = 80 } };
            var cache = new ProcessRecognitionBatchCache(config, cacheFile);
            cache.PutSnapshot(file, "基准", "型材件", input, rows, true); cache.Save();
            input.DocumentTitle = "之后修改"; rows[0].Name = "之后修改";
            var restored = new ProcessRecognitionBatchCache(config, cacheFile);
            Assert.True(restored.TryGetSnapshot(file, "基准", true, out var snapshot, out var processes));
            Assert.Equal("普通编号", snapshot.DocumentTitle);
            Assert.Equal("否", snapshot.PurchaseMarkerSnapshot.Raw);
            Assert.True(Assert.Single(snapshot.Features).IsSubFeature);
            Assert.Equal("框架", Assert.Single(snapshot.Geometry.BodyFacts).BodyName);
            Assert.Equal(new[] { "锯切下料", "检验" }, processes.Select(p => p.Name));
            Assert.True(restored.TryGet(file, "基准", out _, out var summary)); Assert.Equal("锯切下料→检验", summary);
            Assert.False(restored.TryGetSnapshot(file, "另一个配置", true, out _, out _));
            File.AppendAllText(file, " changed");
            Assert.False(restored.TryGetSnapshot(file, "基准", true, out _, out _));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void QuickSnapshotCannotStandInForDeepRecognition()
    {
        var snapshot = ProcessRecognitionSnapshot.Create(new ProcessRecognitionInput { IsPart = true,
            Geometry = new ProcessGeometryFact { GeometryComplete = false, SolidBodyCount = 1 } },
            new[] { new RecognizedProcess { Name = "备料" } }, false);
        var restored = ProcessRecognitionSnapshot.Decode(snapshot.Encode());
        Assert.False(restored.Deep);
        Assert.False(restored.Input.Restore().Geometry.GeometryComplete);
    }
}
