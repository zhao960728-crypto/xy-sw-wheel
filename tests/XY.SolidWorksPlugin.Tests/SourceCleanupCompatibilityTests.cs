using System;
using System.Linq;
using System.Web.Script.Serialization;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class SourceCleanupCompatibilityTests
{
    [Fact]
    public void UnknownConfigurationSurvivesCloneWithoutBecomingAnExecutableFeature()
    {
        var config = ConfigStore.Deserialize("{\"Quote\":{\"Enabled\":true,\"Custom\":[1,2]},\"RadialMenuEnabled\":false}");
        ConfigStore.Normalize(config);
        var copy = ConfigStore.Clone(config);
        Assert.False(copy.RadialMenuEnabled);
        Assert.Null(typeof(PluginConfig).GetProperty("Quote"));
        Assert.False(SwAddin.IsBasicActionEnabled(copy, "quote"));
        var serializer = new JavaScriptSerializer();
        Assert.Equal("{\"Enabled\":true,\"Custom\":[1,2]}", serializer.Serialize(copy.UnknownSettings["Quote"]));
        Assert.DoesNotContain("Quote", ConfigStore.Serialize(new PluginConfig()));
    }

    [Fact]
    public void LegacyModelJournalRemainsReadableForProcessRecognition()
    {
        string json = ValidJournal();
        var record = Assert.Single(ProcessLegacyHoleJournal.Parse(json).Records);
        Assert.Equal("攻牙", ProcessHoleFactBridge.JournalProcessName(record.Spec.Kind));
        Assert.Equal("native-feature-reference", record.ProcessFeature);
        Assert.Null(typeof(ProcessLegacyHoleJournal).GetMethod("Serialize"));
    }

    [Theory]
    [InlineData("\"Version\":1", "\"Version\":2")]
    [InlineData("\"Walls\":[\"wall\"]", "\"Walls\":[]")]
    [InlineData("\"Kind\":1", "\"Kind\":99")]
    [InlineData("\"AppliedDiameterMm\":5", "\"AppliedDiameterMm\":0")]
    public void InvalidLegacyMetadataCannotSupplyConfirmedProcessEvidence(string from, string to)
        => Assert.Throws<InvalidOperationException>(() => ProcessLegacyHoleJournal.Parse(ValidJournal().Replace(from, to)));

    private static string ValidJournal() => "{\"Version\":1,\"Records\":[{\"Id\":\"0123456789abcdef0123456789abcdef\",\"Walls\":[\"wall\"],\"OriginalDiameterMm\":5,\"AppliedDiameterMm\":5,\"ProcessFeature\":\"native-feature-reference\",\"Spec\":{\"Name\":\"M6\",\"Kind\":1}}]}";
}
