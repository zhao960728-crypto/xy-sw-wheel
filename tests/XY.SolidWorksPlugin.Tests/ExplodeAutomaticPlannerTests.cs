using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ExplodeAutomaticPlannerTests
{
 private static ExplodeTarget Box(string name,double x=0,double y=0,double z=0,double size=1)=>new ExplodeTarget {Name=name,Entity=new object(),Bounds=new[]{x,y,z,x+size,y+size,z+size}};
 private static double[] Moved(ExplodeMove move)
 {
  var bounds=(double[])move.Target.Bounds.Clone();double delta=(move.Reverse?-1:1)*move.Distance;
  bounds[move.Axis]+=delta;bounds[move.Axis+3]+=delta;return bounds;
 }
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public void LinearPlansRetainExtentClearanceInEitherDirection(bool reverse)
 {
  var targets=new[]{Box("wide",0,size:10),Box("middle",4,size:5),Box("last",8,size:3)};
  var moves=ExplodedViewPlanner.Plan(targets,.5,0,reverse,false);
  Assert.Equal(0,moves[0].Distance);
  for(int index=1;index<moves.Count;index++)
  {
   var first=Moved(moves[index-1]);var second=Moved(moves[index]);
   double clearance=reverse?first[0]-second[3]:second[0]-first[3];
   Assert.True(clearance>=.5-1e-9);
  }
 }
 [Fact]
 public void AutomaticPlanIsDeterministicKeepsOneAnchorAndDoesNotMutateInputBounds()
 {
  var targets=new[]{Box("core",-.5,-.5,-.5),Box("left",-2,-.5,-.5),Box("right",1,-.5,-.5),Box("top",-.5,1,-.5)};
  var original=targets.Select(target=>(double[])target.Bounds.Clone()).ToArray();
  var first=ExplodedViewPlanner.PlanAutomatic(targets,.000001,CancellationToken.None,out double gap);
  var second=ExplodedViewPlanner.PlanAutomatic(targets,.000001,CancellationToken.None,out double sameGap);
  Assert.Equal(gap,sameGap);Assert.Equal("core",first[0].Target.Name);
  Assert.Single(first.Where(move=>move.Distance==0));
  Assert.Equal(first.Select(move=>(move.Target.Name,move.Axis,move.Reverse,move.Distance)),second.Select(move=>(move.Target.Name,move.Axis,move.Reverse,move.Distance)));
  for(int index=0;index<targets.Length;index++)Assert.Equal(original[index],targets[index].Bounds);
 }
 [Fact]
 public void AutomaticClearanceIsClampedToUsefulMillimeterLimits()
 {
  ExplodedViewPlanner.PlanAutomatic(new[]{Box("a",size:.00001),Box("b",size:.00001)},0,CancellationToken.None,out double small);
  ExplodedViewPlanner.PlanAutomatic(new[]{Box("a",size:100),Box("b",size:100)},0,CancellationToken.None,out double large);
  Assert.Equal(.001,small);Assert.Equal(1,large);
 }
 [Fact]
 public void AutomaticPlanSeparatesAllWorldAndIsometricBoundsForMixedSizes()
 {
  var random=new Random(852);
  var targets=Enumerable.Range(0,90).Select(index=>Box(index.ToString(),random.NextDouble()*8-4,random.NextDouble()*8-4,random.NextDouble()*8-4,.01+random.NextDouble()*8)).ToArray();
  var moves=ExplodedViewPlanner.PlanAutomatic(targets,0,CancellationToken.None,out double gap);
  var world=moves.Select(Moved).ToArray();var screen=world.Select(ExplodedViewPlanner.ProjectIsometric).ToArray();
  for(int first=0;first<world.Length;first++)for(int second=first+1;second<world.Length;second++)
  {
   Assert.True(Enumerable.Range(0,3).Any(axis=>world[first][axis]-world[second][axis+3]>=gap-1e-8||world[second][axis]-world[first][axis+3]>=gap-1e-8));
   Assert.True(Enumerable.Range(0,2).Any(axis=>screen[first][axis]-screen[second][axis+2]>=gap-1e-8||screen[second][axis]-screen[first][axis+2]>=gap-1e-8));
  }
 }
 [Fact]
 public void CoincidentCentersRemainDistinctAndChooseDeterministicAnchor()
 {
  var targets=new[]{Box("a"),Box("b"),Box("c")};
  var moves=ExplodedViewPlanner.PlanAutomatic(targets,0,CancellationToken.None,out double gap);
  Assert.Same(targets[0],moves[0].Target);Assert.Equal(0,moves[0].Distance);
  Assert.True(moves[2].Distance>=moves[1].Distance+1+gap-1e-9);
 }
 [Fact]
 public void PreCancelledPlanningDoesNotReadOrMutateGeometry()
 {
  using var cancel=new CancellationTokenSource();cancel.Cancel();
  Assert.ThrowsAny<OperationCanceledException>(()=>ExplodedViewPlanner.PlanAutomatic(null!,0,cancel.Token,out _));
 }
 [Fact]
 public void WrapperDuplicatesCannotMoveTheSameEntityTwice()
 {
  var first=Box("a");var second=Box("b");second.Entity=first.Entity;
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.Plan(new[]{first,second},.1,0,false,true));
 }
 [Fact]
 public void InvalidExcessiveAndOverflowingBoundsFailBeforeHostChanges()
 {
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.Plan(new[]{Box("a")},.1,0,false,true));
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.Plan(new[]{Box("a"),Box("b",double.NaN)},.1,0,false,true));
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.Plan(Enumerable.Range(0,1001).Select(index=>Box(index.ToString(),index)).ToArray(),.1,0,false,true));
  var enormous=Box("bad");enormous.Bounds=new[]{-double.MaxValue,0d,0,double.MaxValue,1,1};
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.Plan(new[]{Box("a"),enormous},.1,0,false,true));
 }
 [Fact]
 public void FixedAnchorIsKeptAndInvalidAnchorsAreRejected()
 {
  var targets=new[]{Box("a",-2),Box("b"),Box("c",2)};
  var moves=ExplodedViewPlanner.PlanWithAnchor(targets,.1,0,false,true,targets[2]);
  Assert.Same(targets[2],moves[0].Target);Assert.Equal(0,moves[0].Distance);
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.PlanWithAnchor(targets,.1,0,false,true,Box("unknown")));
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewPlanner.PlanWithAnchor(targets,.1,0,false,false,targets[0]));
 }
 [Theory]
 [InlineData(0,0,0,3,false,.000001)]
 [InlineData(2,0,0,2,false,.01)]
 [InlineData(3,2,16,3,true,.0015875)]
 public void NativeLengthPrecisionRoundsDistancesOutward(int unit,int fraction,int denominator,int decimals,bool round,double expected)
 {
  Assert.Equal(expected,ExplodedViewService.LengthQuantum(unit,fraction,denominator,decimals,round),12);
 }
 [Fact]
 public void PartVerificationChecksEveryIdentityIncludingTheUnmovedAnchor()
 {
  var expected=new Dictionary<string,double[]> {["anchor"]=new[]{0d,0,0,1,1,1},["moved"]=new[]{2d,0,0,3,1,1}};
  var actual=expected.ToDictionary(pair=>pair.Key,pair=>(double[])pair.Value.Clone());
  PartExplodeFeatures.VerifyBounds(expected,actual);
  actual["anchor"][0]=.2;
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyBounds(expected,actual));
  actual.Remove("anchor");
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyBounds(expected,actual));
 }
 [Fact]
 public void UntransformedBodiesCannotPassEvenWhenNamesAndCountsMatch()
 {
  var expected=new Dictionary<string,double[]> { ["anchor"]=new[]{0d,0,0,1,1,1},["moved"]=new[]{2d,0,0,3,1,1} };
  var actual=new Dictionary<string,double[]> { ["anchor"]=new[]{0d,0,0,1,1,1},["moved"]=new[]{0d,0,0,1,1,1} };
  var error=Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyBounds(expected,actual));
  Assert.Contains("ExpectedCount=2; ActualCount=2",error.Message);
  Assert.Contains("moved=[2,0,0,3,1,1]",error.Message);Assert.Contains("moved=[0,0,0,1,1,1]",error.Message);
 }
 [Fact]
 public void OutputNameChangesDoNotSilentlySubstituteABoxAtTheExpectedPosition()
 {
  var expected=new Dictionary<string,double[]> { ["original"]=new[]{0d,0,0,1,1,1} };
  var actual=new Dictionary<string,double[]> { ["renamed"]=new[]{0d,0,0,1,1,1} };
  var error=Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyBounds(expected,actual));
  Assert.Contains("original=[",error.Message);Assert.Contains("renamed=[",error.Message);
 }
 [Fact]
 public void NativeGeneratedOutputNamesAreProjectedOnlyAfterACompleteUniqueGeometryMatch()
 {
  var expected=new Dictionary<string,double[]> { ["凸台-拉伸1"]=new[]{-.0392,-.012,0,-.0192,.012,.01},["凸台-拉伸2"]=new[]{0d,0,0,.02,.02,.02},["凸台-拉伸3"]=new[]{.1142,-.014,0,.1342,.014,.012} };
  var actual=new Dictionary<string,double[]> { ["XY一键爆炸[1]"]=expected["凸台-拉伸1"],["凸台-拉伸2"]=expected["凸台-拉伸2"],["XY一键爆炸[2]"]=expected["凸台-拉伸3"] };
  var renames=PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"凸台-拉伸1","凸台-拉伸3"},expected,actual);
  Assert.Equal(2,renames.Count);Assert.Equal("凸台-拉伸1",renames["XY一键爆炸[1]"]);Assert.Equal("凸台-拉伸3",renames["XY一键爆炸[2]"]);
  PartExplodeFeatures.VerifyOutputIdentities("XY一键爆炸",new[]{"凸台-拉伸1","凸台-拉伸3"},expected,actual);
  Assert.True(actual.ContainsKey("XY一键爆炸[1]")); // Planning has no host mutation.
 }
 [Fact]
 public void OutputIdentityVerificationDoesNotChangeNativeLabelsOrBounds()
 {
  var expected=new Dictionary<string,double[]> { ["anchor"]=new[]{0d,0,0,1,1,1},["input"]=new[]{2d,0,0,3,1,1} };
  var actual=new Dictionary<string,double[]> { ["anchor"]=(double[])expected["anchor"].Clone(),["XY一键爆炸[1]"]=(double[])expected["input"].Clone() };
  var before=actual.ToDictionary(pair=>pair.Key,pair=>(double[])pair.Value.Clone());
  PartExplodeFeatures.VerifyOutputIdentities("XY一键爆炸",new[]{"input"},expected,actual);
  Assert.Equal(before.Keys,actual.Keys);foreach(var pair in before)Assert.Equal(pair.Value,actual[pair.Key]);
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyOutputIdentities("ForeignFeature",new[]{"input"},expected,actual));
  actual["anchor"][0]=.2;
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyOutputIdentities("XY一键爆炸",new[]{"input"},expected,actual));
 }
 [Theory]
 [InlineData("AnotherFeature[1]")]
 [InlineData("XY一键爆炸旧[1]")]
 [InlineData("XY一键爆炸[01]")]
 [InlineData("XY一键爆炸[0]")]
 [InlineData("XY一键爆炸[1]suffix")]
 public void ForeignOrForgedOutputPrefixesCannotSupplyAMissingBody(string outputName)
 {
  var box=new[]{2d,0,0,3,1,1};
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"moved"},new Dictionary<string,double[]> { ["moved"]=box },new Dictionary<string,double[]> { [outputName]=box }));
 }
 [Fact]
 public void CurrentFeatureOutputWithWrongGeometryIsRejected()
 {
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"moved"},new Dictionary<string,double[]> { ["moved"]=new[]{2d,0,0,3,1,1} },new Dictionary<string,double[]> { ["XY一键爆炸[1]"]=new[]{0d,0,0,1,1,1} }));
 }
 [Fact]
 public void RenamedUnmovedAnchorCannotBeAcceptedEvenWithItsCorrectBox()
 {
  var box=new[]{0d,0,0,1,1,1};
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",Array.Empty<string>(),new Dictionary<string,double[]> { ["anchor"]=box },new Dictionary<string,double[]> { ["XY一键爆炸[1]"]=box }));
 }
 [Fact]
 public void CoincidentOutputBoundsCannotGuessBetweenTwoMissingInputIdentities()
 {
  var box=new[]{2d,0,0,3,1,1};
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"a","b"},new Dictionary<string,double[]> { ["a"]=box,["b"]=box },new Dictionary<string,double[]> { ["XY一键爆炸[1]"]=box,["XY一键爆炸[2]"]=box }));
 }
 [Fact]
 public void ExistingInputNameWithWrongBoxCannotBeRelabeledToAnotherInput()
 {
  var first=new[]{0d,0,0,1,1,1};var second=new[]{2d,0,0,3,1,1};
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"a","b"},new Dictionary<string,double[]> { ["a"]=first,["b"]=second },new Dictionary<string,double[]> { ["a"]=second,["XY一键爆炸[1]"]=first }));
 }
 [Fact]
 public void MissingBodyCannotPassByRestoringOneRemainingOutputLabel()
 {
  var box=new[]{0d,0,0,1,1,1};
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.PlanOutputNameRestoration("XY一键爆炸",new[]{"a"},new Dictionary<string,double[]> { ["a"]=box,["anchor"]=box },new Dictionary<string,double[]> { ["XY一键爆炸[1]"]=box }));
 }
 [Fact]
 public void GeometryDiagnosticsUseInvariantCoordinatesAndIncludeMissingBodyCounts()
 {
  var expected=new Dictionary<string,double[]> { ["missing"]=new[]{.125,0d,0d,1d,1d,1d} };
  var error=Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.VerifyBounds(expected,new Dictionary<string,double[]>()));
  Assert.Contains("ExpectedCount=1; ActualCount=0",error.Message);Assert.Contains("0.125",error.Message);
 }
 [Fact]
 public void PartRepeatParametersMatchByStableNameAndSignedPreciseDelta()
 {
  const string before="[{\"Name\":\"左板[1]\",\"Delta\":[-0.012345678,0,0]},{\"Name\":\"右板[1]\",\"Delta\":[0,0,0.2]}]";
  const string reordered="[{\"Name\":\"右板[1]\",\"Delta\":[0,0,0.2]},{\"Name\":\"左板[1]\",\"Delta\":[-0.012345678,0,0]}]";
  Assert.True(PartExplodeFeatures.SameParameters(before,reordered));
  Assert.False(PartExplodeFeatures.SameParameters(before,reordered.Replace("-0.012345678","0.012345678")));
  Assert.False(PartExplodeFeatures.SameParameters(before,reordered.Replace("右板[1]","其他实体")));
 }
 [Fact]
 public void LegacyMultiConfigurationDefinitionCanBeReusedButItsUnknownOwnerCannotBeReplaced()
 {
  PartExplodeFeatures.ValidateDefinitionUpdate("","A",new[]{"A","B"},true);
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.ValidateDefinitionUpdate("","A",new[]{"A","B"},false));
  PartExplodeFeatures.ValidateDefinitionUpdate("","A",new[]{"A"},false);
  PartExplodeFeatures.ValidateDefinitionUpdate("A","A",new[]{"A","B"},false);
 }
 [Fact]
 public void SavedDefinitionOwnerCannotBeOverriddenByAnotherConfiguration()
 {
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.ValidateDefinitionUpdate("A","B",new[]{"A","B"},false));
  Assert.Throws<InvalidOperationException>(()=>PartExplodeFeatures.ValidateDefinitionUpdate("A","B",new[]{"A","B"},true));
 }
 [Theory]
 [InlineData("null")]
 [InlineData("[]")]
 [InlineData("[null]")]
 [InlineData("not json")]
 [InlineData("[{\"Name\":\"\",\"Delta\":[0,0,1]}]")]
 [InlineData("[{\"Name\":\"A\",\"Delta\":[0,1]}]")]
 [InlineData("[{\"Name\":\"A\",\"Delta\":[0,0,1]},{\"Name\":\"A\",\"Delta\":[1,0,0]}]")]
 public void InvalidSavedMovementsCannotReachNativeGeometry(string json)=>Assert.Throws<InvalidOperationException>(()=>ExplodeMacroFeature.Parse(json));
}

