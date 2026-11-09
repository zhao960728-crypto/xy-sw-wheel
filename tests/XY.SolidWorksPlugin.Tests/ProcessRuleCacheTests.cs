using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRuleCacheTests
{
    [Theory]
    [InlineData("custom-rule")]
    [InlineData("type-group")]
    [InlineData("type-chain")]
    [InlineData("schema")]
    public void SemanticChangeDoesNotReturnSavedRecognitionResult(string change)
    {
        string directory = Path.Combine(Path.GetTempPath(), "xy-sw-process-rule-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string model = Path.Combine(directory, "sample.sldprt"), cacheFile = Path.Combine(directory, "cache.json");
        try
        {
            File.WriteAllText(model, "cache fingerprint fixture");
            var config = new ProcessRecognitionConfig();
            var cache = new ProcessRecognitionBatchCache(config, cacheFile);
            cache.Put(model, "默认", PartTypes.SheetMetal, "旧识别工序"); cache.Save();
            Assert.True(new ProcessRecognitionBatchCache(config, cacheFile).TryGet(model, "默认", out _, out string unchanged));
            Assert.Equal("旧识别工序", unchanged);
            if (change == "custom-rule") config.CustomRules.Add(new ProcessRecognitionRule { Name = "新增检验", NameContains = "sample", ProcessNames = "清洗", BeforeProcess = "检验" });
            if (change == "type-group") config.ProcessTypeGroups = "方通=总焊";
            if (change == "type-chain") config.PartTypeChains = "钣金件=下料>检验>清洗";
            if (change == "schema")
            {
                var fingerprint = typeof(ProcessRecognitionBatchCache).GetMethod("FingerprintValue", BindingFlags.NonPublic | BindingFlags.Static)!;
                string oldFingerprint = (string)fingerprint.Invoke(null, new object[] { config, "process-recognition-v12" })!;
                var serializer = new JavaScriptSerializer();
                var document = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(File.ReadAllText(cacheFile));
                foreach (var entry in document.Values) entry["Fingerprint"] = oldFingerprint;
                File.WriteAllText(cacheFile, serializer.Serialize(document));
            }
            Assert.False(new ProcessRecognitionBatchCache(config, cacheFile).TryGet(model, "默认", out _, out _));
        }
        finally
        {
            if (File.Exists(cacheFile)) File.Delete(cacheFile);
            if (File.Exists(model)) File.Delete(model);
            Directory.Delete(directory);
        }
    }
}
