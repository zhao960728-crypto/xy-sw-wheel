using System;
using System.Linq;
using SolidWorks.Interop.swconst;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessHoleFactBridgeTests
{
    [Fact]
    public void NativeExternalOrCosmeticThreadDoesNotConfirmTapping()
    {
        var facts = ProcessHoleFactBridge.FromFeatures(new[] { new ProcessFeatureFact { TypeName = "CosmeticThread", Name = "External screw" }, new ProcessFeatureFact { TypeName = "Thread", Name = "Thread1" } });
        Assert.All(facts, f => { Assert.False(f.IsConfirmed); Assert.Equal("螺纹加工", f.ProcessName); });
    }
    [Fact]
    public void NameOnlyRivetAndTappingStayCandidates()
    {
        var facts = ProcessHoleFactBridge.FromFeatures(new[] {
            new ProcessFeatureFact { Name = "压铆螺母候选", TypeName = "Cut" },
            new ProcessFeatureFact { Name = "攻牙待核", TypeName = "Cut" } });
        Assert.Equal(2, facts.Length);
        Assert.All(facts, f => { Assert.False(f.IsConfirmed); Assert.Equal("FeatureCandidate", f.Source); });
    }
    [Fact]
    public void NativeHoleWizardDefinitionDoesNotNeedNameGuess()
    {
        int type = (int)swWzdHoleTypes_e.swTapBlind;
        var fact = Assert.Single(ProcessHoleFactBridge.FromFeatures(new[] { new ProcessFeatureFact { Name = "Feature1", TypeName = "HoleWzd", HoleWizardType = type } }));
        Assert.True(fact.IsConfirmed); Assert.Equal("攻牙", fact.ProcessName); Assert.Equal("NativeFeature", fact.Source);
    }
    [Fact]
    public void SuppressedNativeEvidenceCannotConfirmAnything()
    {
        Assert.Empty(ProcessHoleFactBridge.FromFeatures(new[] { new ProcessFeatureFact { Name = "攻牙", TypeName = "CosmeticThread", IsSuppressed = true } }));
    }
    [Fact]
    public void DeduplicationDistinguishesConfirmationSourceAndReference()
    {
        var a = new ProcessHoleFact { ProcessName = "压铆", Source = "NativeJournal", IsConfirmed = true, Reference = "one" };
        var b = new ProcessHoleFact { ProcessName = "压铆", Source = "FeatureCandidate", IsConfirmed = false, Reference = "one" };
        Assert.False(ProcessHoleFactBridge.SameFacts(new[] { a }, new[] { b }));
        Assert.True(ProcessHoleFactBridge.SameFacts(new[] { a, b }, new[] { b, a }));
        b.Reference = "two";
        Assert.False(ProcessHoleFactBridge.SameFacts(new[] { a }, new[] { b }));
    }
}
