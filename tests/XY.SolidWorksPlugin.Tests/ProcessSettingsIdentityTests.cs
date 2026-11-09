using System.Collections.Generic;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessSettingsIdentityTests
{
    [Fact]
    public void PropertyOrderDoesNotInvalidateSettingsButRuleOrderDoes()
    {
        var a = new Dictionary<string, object> { ["enabled"] = true, ["rules"] = new[] {
            new Dictionary<string, object> { ["name"] = "A", ["process"] = "压铆" },
            new Dictionary<string, object> { ["name"] = "B", ["process"] = "攻牙" } } };
        var b = new Dictionary<string, object> { ["rules"] = new[] {
            new Dictionary<string, object> { ["process"] = "压铆", ["name"] = "A" },
            new Dictionary<string, object> { ["process"] = "攻牙", ["name"] = "B" } }, ["enabled"] = true };
        Assert.Equal(ProcessRecognitionBatchCache.CanonicalJson(a), ProcessRecognitionBatchCache.CanonicalJson(b));
        b["rules"] = new[] {
            new Dictionary<string, object> { ["name"] = "B", ["process"] = "攻牙" },
            new Dictionary<string, object> { ["name"] = "A", ["process"] = "压铆" } };
        Assert.NotEqual(ProcessRecognitionBatchCache.CanonicalJson(a), ProcessRecognitionBatchCache.CanonicalJson(b));
    }
}
