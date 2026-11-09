using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessSettingsPerformanceTests
{
    [Fact]
    public void DirectSettingsEncodingMatchesPriorCanonicalValueWithNestedRules()
    {
        var config = new ProcessRecognitionConfig { SummaryPropertyName = "工序\"\r\n\t\\", CustomRules = new List<ProcessRecognitionRule> {
            new ProcessRecognitionRule { Name = "规则A", MinBodyCount = null, MaxBodyCount = 3, HasWeldEvidence = false, ProcessNames = "下料>终检" },
            new ProcessRecognitionRule { Name = "规则B", MinBodyCount = 0, MaxBodyCount = null, HasWeldEvidence = null, ProcessNames = "折弯" } } };
        Assert.Equal(LegacyCanonical(config), ProcessRecognitionBatchCache.SettingsKey(config));
        var copy = new JavaScriptSerializer().Deserialize<ProcessRecognitionConfig>(new JavaScriptSerializer().Serialize(config));
        Assert.Equal(ProcessRecognitionBatchCache.SettingsKey(config), ProcessRecognitionBatchCache.SettingsKey(copy));
    }

    [Fact]
    public void EveryPersistedScalarAndInPlaceRuleChangeStillInvalidatesSettings()
    {
        foreach (PropertyInfo property in typeof(ProcessRecognitionConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var config = new ProcessRecognitionConfig();
            string before = ProcessRecognitionBatchCache.SettingsKey(config);
            if (property.PropertyType == typeof(string)) property.SetValue(config, (string)property.GetValue(config) + "改变");
            else if (property.PropertyType == typeof(bool)) property.SetValue(config, !(bool)property.GetValue(config));
            else if (property.PropertyType == typeof(int)) property.SetValue(config, (int)property.GetValue(config) + 1);
            else if (property.PropertyType == typeof(List<ProcessRecognitionRule>)) config.CustomRules.Add(new ProcessRecognitionRule { Name = "新增" });
            else throw new InvalidOperationException("Uncovered settings type: " + property.Name);
            Assert.NotEqual(before, ProcessRecognitionBatchCache.SettingsKey(config));
            Assert.Equal(LegacyCanonical(config), ProcessRecognitionBatchCache.SettingsKey(config));
        }
        var mutable = new ProcessRecognitionConfig { CustomRules = new List<ProcessRecognitionRule> { new ProcessRecognitionRule { Name = "A" }, new ProcessRecognitionRule { Name = "B" } } };
        string original = ProcessRecognitionBatchCache.SettingsKey(mutable);
        mutable.CustomRules[0].MinBendCount = 2;
        string modified = ProcessRecognitionBatchCache.SettingsKey(mutable);
        Assert.NotEqual(original, modified);
        mutable.CustomRules.Reverse();
        Assert.NotEqual(modified, ProcessRecognitionBatchCache.SettingsKey(mutable));
        Assert.Equal(LegacyCanonical(mutable), ProcessRecognitionBatchCache.SettingsKey(mutable));
    }

    [Fact]
    public void GeneralDictionariesNullAndPrimitiveRulesKeepTheSameCanonicalValue()
    {
        var data = new Dictionary<string, object> { ["z"] = null!, ["a"] = new object[] { true, 0, 2.5, "引号\"", null! },
            ["nested"] = new Dictionary<string, string> { ["b"] = "B", ["a"] = "A" } };
        Assert.Equal(LegacyCanonical(data), ProcessRecognitionBatchCache.CanonicalJson(data));
    }

    private static string LegacyCanonical(object value)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string Encode(object item)
        {
            if (item is IDictionary<string, object> map) return "{" + string.Join(",", map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => serializer.Serialize(pair.Key) + ":" + Encode(pair.Value))) + "}";
            if (item is IEnumerable list && !(item is string)) return "[" + string.Join(",", list.Cast<object>().Select(Encode)) + "]";
            return serializer.Serialize(item);
        }
        return Encode(serializer.DeserializeObject(serializer.Serialize(value)));
    }
}
