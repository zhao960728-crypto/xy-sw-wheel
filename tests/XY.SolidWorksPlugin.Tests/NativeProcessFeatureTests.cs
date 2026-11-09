using System.Linq;
using SolidWorks.Interop.swconst;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class NativeProcessFeatureTests
{
    [Theory]
    [InlineData("Hem", "压死边")]
    [InlineData("Jog", "段差折弯")]
    [InlineData("Chamfer", "倒角")]
    public void RenamedNativeFeatureRetainsProcessEvidence(string type, string expected)
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] { new ProcessFeatureFact { Name = "Feature001", TypeName = type, HemType = (int)swHemTypes_e.swHemTypeClosed } } };
        var process = Assert.Single(ProcessRecognitionService.Recognize(input, false), row => row.Name == expected);
        Assert.Contains(type, process.Evidence);
    }

    [Theory]
    [InlineData("Hem", "压死边")]
    [InlineData("Jog", "段差折弯")]
    [InlineData("Chamfer", "倒角")]
    public void SuppressedOrMerelyNamedFeaturesDoNotAddNativeOperations(string type, string expected)
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] {
            new ProcessFeatureFact { Name = "Feature001", TypeName = type, IsSuppressed = true },
            new ProcessFeatureFact { Name = type, TypeName = "RefPlane" }
        } };
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, true), row => row.Name == expected);
    }

    [Theory]
    [InlineData(swWzdHoleTypes_e.swPipeTapBlind, true, false)]
    [InlineData(swWzdHoleTypes_e.swTapBlindCosmeticThreadCounterSinkTop, true, true)]
    [InlineData(swWzdHoleTypes_e.swCounterBoreSlotThru, false, true)]
    [InlineData(swWzdHoleTypes_e.swSlotThru, false, false)]
    public void ActualWizardKindsKeepThreadAndRecessIndependent(swWzdHoleTypes_e type, bool tapped, bool recessed)
    {
        Assert.Equal(tapped, ProcessRecognitionService.WizardHoleIsTapped((int)type));
        Assert.Equal(recessed, ProcessRecognitionService.WizardHoleHasRecess((int)type));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(80)]
    [InlineData(999)]
    public void UnknownWizardKindDoesNotInventOperation(int type)
    {
        Assert.False(ProcessRecognitionService.WizardHoleIsTapped(type));
        Assert.False(ProcessRecognitionService.WizardHoleHasRecess(type));
    }

    [Theory]
    [InlineData(swHemTypes_e.swHemTypeOpen)]
    [InlineData(swHemTypes_e.swHemTypeRolled)]
    [InlineData(swHemTypes_e.swHemTypeTearDrop)]
    public void NonClosedHemIsNotAssumedToBeDeadFold(swHemTypes_e type)
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] {
            new ProcessFeatureFact { Name = "褶边1", TypeName = "Hem", HemType = (int)type }
        } };
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, false), row => row.Name == "压死边");
    }

    [Fact]
    public void SheetMetalEdgeChamferDoesNotAutomaticallyRequireMachining()
    {
        var input = new ProcessRecognitionInput { IsPart = true, Features = new[] {
            new ProcessFeatureFact { Name = "Feature001", TypeName = "SMBaseFlange" },
            new ProcessFeatureFact { Name = "Feature002", TypeName = "Chamfer" }
        } };
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, false), row => row.Name == "倒角");
    }
}
