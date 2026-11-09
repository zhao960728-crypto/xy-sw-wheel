using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

// These proxies execute the production service's native-view transaction without
// connecting to SOLIDWORKS. Unexpected geometry/Transform writes fail immediately.
public sealed class ExplodeAutomaticTransactionTests
{
 [Fact]
 public void FreshServicesReuseVerifiedNativeStepsWithoutAppendingOrWritingTransforms()
 {
  var fixture=new AssemblyFixture();
  string first=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  var next=new ExplodedViewService(fixture.Host);
  string second=next.CreateAllAutomatic();
  Assert.Equal(first,second);Assert.True(next.LastAutomaticWasReused);
  Assert.Single(fixture.Views);Assert.Equal(1,fixture.CreateCount);Assert.Equal(2,fixture.Views[first].Count);
  Assert.Equal(2,fixture.SuspendCount);Assert.Equal(2,fixture.ResumeCount);
  Assert.Equal(0,fixture.TransformWrites);
 }
 [Fact]
 public void AUserViewWithRotationIsNotReusedOrEdited()
 {
  var fixture=new AssemblyFixture();
  string original=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  fixture.Views[original][0].Rotation=.2;
  string before=fixture.Fingerprint(original);
  string created=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  Assert.NotEqual(original,created);Assert.Equal(2,fixture.Views.Count);
  Assert.Equal(before,fixture.Fingerprint(original));Assert.Equal(0,fixture.TransformWrites);
 }
 [Fact]
 public void FailedNativeStepDeletesOnlyNewViewAndRestoresPriorExpansionHiddenStateAndSelection()
 {
  var fixture=new AssemblyFixture();fixture.SeedPriorView();fixture.Components[1].Hidden=true;
  fixture.FailAtStep=2;
  Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal(new[]{"UserPrior"},fixture.Views.Keys);Assert.Equal("UserPrior",fixture.Expanded);
  Assert.True(fixture.Components[1].Hidden);Assert.Equal(1,fixture.SuspendCount);Assert.Equal(1,fixture.ResumeCount);
  Assert.Equal(1,fixture.DeleteCount);Assert.Equal(0,fixture.TransformWrites);
 }
 [Fact]
 public void EscapeFromProgressUsesTheSameRollbackAndRetainsCancellationIdentity()
 {
  var fixture=new AssemblyFixture();fixture.SeedPriorView();fixture.Components[1].Hidden=true;
  using var cancel=new CancellationTokenSource();
  Assert.ThrowsAny<OperationCanceledException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic(cancel.Token,message=>
  {
   if(message.StartsWith("正在创建爆炸步骤 2/",StringComparison.Ordinal)){cancel.Cancel();cancel.Token.ThrowIfCancellationRequested();}
  }));
  Assert.Equal(new[]{"UserPrior"},fixture.Views.Keys);Assert.Equal("UserPrior",fixture.Expanded);
  Assert.True(fixture.Components[1].Hidden);Assert.Equal(1,fixture.DeleteCount);
  Assert.Equal(fixture.SuspendCount,fixture.ResumeCount);
 }
 [Fact]
 public void EverySuccessfulNativeStepIsRebuiltBeforeAnotherStepOrFinalValidation()
 {
  var fixture=new AssemblyFixture();
  string name=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  Assert.Equal(new[]{"add:"+name+":1","rebuild:"+name+":1","add:"+name+":2","rebuild:"+name+":2","rebuild:"+name+":2"},fixture.NativeStepTrace);
  Assert.Equal(new[]{false,true},fixture.ComponentQueries);
  Assert.Equal(1,fixture.SuspendCount);Assert.Equal(1,fixture.ResumeCount);
  Assert.Equal(0,fixture.TransformWrites);Assert.Equal(0,fixture.DeleteCount);
 }
 [Fact]
 public void CancellationCannotDeleteANativeViewBeforeItsCompletedFirstStepWasRebuilt()
 {
  var fixture=new AssemblyFixture {RequireRebuildBeforeDelete=true};
  using var cancel=new CancellationTokenSource();
  Assert.ThrowsAny<OperationCanceledException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic(cancel.Token,message=>
  {
   if(message.StartsWith("正在创建爆炸步骤 2/",StringComparison.Ordinal))cancel.Cancel();
  }));
  Assert.Equal(new[]{"add:ExplView1:1","rebuild:ExplView1:1","delete:ExplView1"},fixture.NativeStepTrace);
  Assert.Equal(1,fixture.DeleteCount);Assert.Empty(fixture.Views);Assert.Equal("",fixture.Expanded);
  Assert.Equal(1,fixture.SuspendCount);Assert.Equal(1,fixture.ResumeCount);Assert.Equal(0,fixture.TransformWrites);
 }
 [Fact]
 public void PreCancelledCommandDoesNotSuspendSelectionOrCollapseAnExistingView()
 {
  var fixture=new AssemblyFixture();fixture.SeedPriorView();using var cancel=new CancellationTokenSource();cancel.Cancel();
  var service=new ExplodedViewService(fixture.Host);
  Assert.ThrowsAny<OperationCanceledException>(()=>service.CreateAllAutomatic(cancel.Token));
  Assert.Equal("UserPrior",fixture.Expanded);Assert.Equal(0,fixture.ShowCount);
  Assert.Equal(0,fixture.SuspendCount);Assert.Equal(0,fixture.CreateCount);Assert.Equal(0,fixture.ComponentReads);
 }
 [Fact]
 public void SuppressedLeafRejectsTheEntireScopeWithoutPartialExplosion()
 {
  var fixture=new AssemblyFixture();fixture.Components[0].Suppression=0;fixture.Components[1].Hidden=true;
  Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal(0,fixture.CreateCount);Assert.Empty(fixture.Views);Assert.True(fixture.Components[1].Hidden);
  Assert.Equal(fixture.SuspendCount,fixture.ResumeCount);
 }
 [Fact]
 public void SuppressedPreflightDoesNotAccessCollapsedViewOrSelectionState()
 {
  var fixture=new AssemblyFixture();fixture.SeedPriorView();fixture.Expanded="";
  fixture.Components[2].Suppression=0;fixture.Components[1].Hidden=true;
  fixture.FailRepeatedIsExpanded=true;
  Assert.True(fixture.Components[0].Proxy.Select4(false,null,false));
  string prior=fixture.Fingerprint("UserPrior");
  var error=Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Contains("已抑制",error.Message);Assert.Equal(new[]{false},fixture.ComponentQueries);
  Assert.Equal(new[]{"UserPrior"},fixture.Views.Keys);Assert.Equal(prior,fixture.Fingerprint("UserPrior"));
  Assert.Equal("",fixture.Expanded);Assert.Equal(0,fixture.ShowCount);Assert.Equal(0,fixture.CreateCount);Assert.Equal(0,fixture.DeleteCount);
  Assert.True(fixture.Components[1].Hidden);Assert.Equal(0,fixture.Components[2].Suppression);
  AssertNoViewOrSelectionTransaction(fixture);Assert.Equal(fixture.Components[0].Id,fixture.SelectedComponentId);
 }
 [Fact]
 public void SuppressedPreflightLeavesAnExpandedPriorViewCompletelyUntouched()
 {
  var fixture=new AssemblyFixture();fixture.SeedPriorView();fixture.Components[2].Suppression=0;
  Assert.True(fixture.Components[0].Proxy.Select4(false,null,false));
  Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal("UserPrior",fixture.Expanded);AssertNoViewOrSelectionTransaction(fixture);
  Assert.Equal(new[]{"UserPrior"},fixture.Views.Keys);Assert.Equal(0,fixture.CreateCount);Assert.Equal(0,fixture.DeleteCount);
  Assert.Equal(fixture.Components[0].Id,fixture.SelectedComponentId);
 }
 [Fact]
 public void SuppressedPreflightDoesNotResolveOtherLightweightComponents()
 {
  var fixture=new AssemblyFixture();fixture.Components[0].Suppression=1;fixture.Components[0].LightweightModelUnavailable=true;
  fixture.Components[2].Suppression=0;
  Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal(1,fixture.Components[0].Suppression);
  Assert.All(fixture.Components,component=>{Assert.Empty(component.SuppressionWrites);Assert.Equal(0,component.ModelReads);});
  AssertNoViewOrSelectionTransaction(fixture);
 }
 [Fact]
 public void SuppressedPreflightIncludesNestedComponentsFromTheFlatEnumeration()
 {
  var fixture=new AssemblyFixture();
  var nested=new FakeComponent(4,.06,_=>{},()=>{}) {Suppression=0};
  fixture.FlatComponents=fixture.Components.Select(component=>(object)component.Proxy).Concat(new object[]{nested.Proxy}).ToArray();
  var error=Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Contains("已抑制：Part4",error.Message);Assert.Equal(1,nested.SuppressedReads);
  Assert.Equal(new[]{false},fixture.ComponentQueries);AssertNoViewOrSelectionTransaction(fixture);
 }
 [Fact]
 public void SuppressedPreflightKeepsTheExistingEightIssueLimitAndRemainderCount()
 {
  var fixture=new AssemblyFixture(10);
  foreach(var component in fixture.Components)component.Suppression=0;
  var error=Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal("无法完整爆炸全部对象，请先处理："+string.Join("；",Enumerable.Range(1,8).Select(id=>"已抑制：Part"+id))
   +"；另有 2 项。未生成部分爆炸图。",error.Message);
  AssertNoViewOrSelectionTransaction(fixture);
 }
 [Fact]
 public void OversizedFlatPreflightRejectsBeforeAnyViewOrSelectionAccess()
 {
  var fixture=new AssemblyFixture();
  fixture.FlatComponents=Enumerable.Repeat((object)fixture.Components[0].Proxy,10001).ToArray();
  var error=Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Contains("装配体层级范围过大",error.Message);
  Assert.All(fixture.Components,component=>Assert.Equal(0,component.SuppressedReads));
  AssertNoViewOrSelectionTransaction(fixture);
 }
 [Fact]
 public void FlatPreflightHonorsCancellationWithoutOpeningAViewOrSelectionTransaction()
 {
  var fixture=new AssemblyFixture(40);using var cancel=new CancellationTokenSource();
  fixture.Components[0].OnSuppressedRead=()=>cancel.Cancel();
  Assert.ThrowsAny<OperationCanceledException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic(cancel.Token));
  Assert.Equal(32,fixture.Components.Sum(component=>component.SuppressedReads));
  AssertNoViewOrSelectionTransaction(fixture);
 }
 private static void AssertNoViewOrSelectionTransaction(AssemblyFixture fixture)
 {
  Assert.Equal(0,fixture.IsExpandedReads);Assert.Equal(0,fixture.ViewNamesReads);Assert.Equal(0,fixture.ShowCount);
  Assert.Equal(0,fixture.SelectionManagerReads);Assert.Equal(0,fixture.ClearSelectionCount);
  Assert.Equal(0,fixture.SuspendCount);Assert.Equal(0,fixture.ResumeCount);
  Assert.Equal(0,fixture.CreateCount);Assert.Equal(0,fixture.DeleteCount);Assert.Equal(0,fixture.TransformWrites);
 }
 [Fact]
 public void CancelAfterResolvingLightweightRestoresOnlyItsOriginalResolutionState()
 {
  var fixture=new AssemblyFixture();fixture.Components[0].Suppression=1;fixture.Components[0].LightweightModelUnavailable=true;
  using var cancel=new CancellationTokenSource();
  Assert.ThrowsAny<OperationCanceledException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic(cancel.Token,message=>
  {
   if(message.StartsWith("正在规划",StringComparison.Ordinal)){cancel.Cancel();cancel.Token.ThrowIfCancellationRequested();}
  }));
  Assert.Equal(1,fixture.Components[0].Suppression);Assert.Equal(new[]{2,1},fixture.Components[0].SuppressionWrites);
  Assert.Equal(0,fixture.CreateCount);Assert.Empty(fixture.Views);
 }
 [Fact]
 public void MoreThanAThousandLeavesFailsBeforeCreatingAnyView()
 {
  var fixture=new AssemblyFixture(1001);
  Assert.Throws<InvalidOperationException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic());
  Assert.Equal(0,fixture.CreateCount);Assert.Empty(fixture.Views);
  Assert.Equal(fixture.SuspendCount,fixture.ResumeCount);
 }
 [Fact]
 public void ChangedNativeStepIdentityCannotBeMistakenForAReusableView()
 {
  var fixture=new AssemblyFixture();
  string original=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  fixture.Views[original][0].Component=fixture.Components[0];
  fixture.Views[original][1].Component=fixture.Components[0];
  string fingerprint=fixture.Fingerprint(original);
  string created=new ExplodedViewService(fixture.Host).CreateAllAutomatic();
  Assert.NotEqual(original,created);Assert.Equal(fingerprint,fixture.Fingerprint(original));Assert.Equal(2,fixture.Views.Count);
 }
 [Theory]
 [InlineData(true)]
 [InlineData(false)]
 public void PartCancellationAfterMacroDeletionRestoresHiddenStateOnFreshBodies(bool persistentReferenceSurvives)
 {
  var fixture=new PartFixture {PersistentReferenceSurvives=persistentReferenceSurvives};
  var oldHidden=fixture.Bodies[1];oldHidden.Hidden=true;
  using var cancel=new CancellationTokenSource();bool inserted=false;
  Assert.ThrowsAny<OperationCanceledException>(()=>new ExplodedViewService(fixture.Host).CreateAllAutomatic(cancel.Token,message=>
  {
   if(fixture.FeatureExists){inserted=true;cancel.Cancel();}
  }));
  Assert.True(inserted);Assert.False(fixture.FeatureExists);Assert.Equal(1,fixture.DeleteCount);
  Assert.NotSame(oldHidden,fixture.Bodies[1]);Assert.True(oldHidden.Stale);Assert.Equal(0,oldHidden.StaleHideWrites);
  Assert.True(fixture.Bodies[1].Hidden);Assert.False(fixture.Bodies[0].Hidden);Assert.False(fixture.Bodies[2].Hidden);
  Assert.Equal(1,fixture.SuspendCount);Assert.Equal(1,fixture.ResumeCount);
 }
 [Fact]
 public void OldPersistentWrapperCannotBeUsedAfterTheNativeBodyWasReplaced()
 {
  var stale=new FakeBody(1,"Hidden",new[]{0d,0,0,1,1,1}) {Stale=true};
  var current=new FakeBody(1,"Hidden",new[]{0d,0,0,1,1,1});
  var identity=new ExplodedViewService.BodyVisibilityIdentity("Hidden",new[]{0d,0,0,1,1,1},new byte[]{1});
  Assert.Same(current.Proxy,ExplodedViewService.ResolveVisibilityBody(identity,new IBody2[]{current.Proxy},_=>stale.Proxy));
  Assert.Equal(0,stale.StaleHideWrites);
 }
 [Fact]
 public void FreshPersistentBodyMustStillMatchTheCapturedOriginalBounds()
 {
  var shifted=new FakeBody(1,"Hidden",new[]{2d,0,0,3,1,1});
  var identity=new ExplodedViewService.BodyVisibilityIdentity("Hidden",new[]{0d,0,0,1,1,1},new byte[]{1});
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewService.ResolveVisibilityBody(identity,new IBody2[]{shifted.Proxy},_=>shifted.Proxy));
 }
 [Fact]
 public void NameFallbackRefusesAmbiguousBodiesInsteadOfHidingTheFirstMatch()
 {
  var first=new FakeBody(1,"Hidden",new[]{0d,0,0,1,1,1});var second=new FakeBody(2,"Hidden",new[]{0d,0,0,1,1,1});
  var identity=new ExplodedViewService.BodyVisibilityIdentity("Hidden",new[]{0d,0,0,1,1,1},null!);
  Assert.Throws<InvalidOperationException>(()=>ExplodedViewService.ResolveVisibilityBody(identity,new IBody2[]{first.Proxy,second.Proxy},_=>null!));
  Assert.False(first.Hidden);Assert.False(second.Hidden);
 }
 [Fact]
 public void MacroRebuildRejectsMissingTemporaryNamesWithExpectedAndActualIdentityEvidence()
 {
  var unnamed=new FakeBody(1,"Original",new[]{0d,0,0,1,1,1}) {Stale=true};
  var data=Com<MacroFeatureData>(call=>
  {
   switch(call.MethodName)
   {
    case "set_EnableMultiBodyConsume":return null;
    case "GetStringByName":return new Out(null,1,"[{\"Name\":\"Original\",\"Delta\":[1,0,0]}]");
    case "get_EditBodies":return new object[]{unnamed.Proxy};
    case "GetEditBodiesCount":return 1;
    default:return Unexpected(call);
   }
  });
  var feature=Com<Feature>(call=>call.MethodName=="GetDefinition"?data:call.MethodName=="get_Name"?"DiagnosticMacro":Unexpected(call));
  object result=new ExplodeMacroFeature().Regenerate(new object(),new object(),feature);
  string error=Assert.IsType<string>(result);Assert.Contains("Expected=Original",error);Assert.Contains("Actual=",error);
 }
 private sealed class PartFixture
 {
  internal readonly ISldWorks Host;
  internal readonly List<FakeBody> Bodies=new List<FakeBody>();
  internal bool FeatureExists,PersistentReferenceSurvives=true;
  internal int DeleteCount,SuspendCount,ResumeCount;
  private string _featureName="MacroFeature1",_json="";
  private readonly List<FakeBody> _versions=new List<FakeBody>();
  internal PartFixture()
  {
   for(int index=0;index<3;index++)Bodies.Add(new FakeBody(index+1,"Body"+(index+1),new[]{index*.02,0d,0d,index*.02+.01,.01,.01}));
   _versions.AddRange(Bodies);
   var definition=Com<MacroFeatureData>(call=>
   {
    switch(call.MethodName)
    {
     case "GetBaseName":return "XYExplodeV1";
     case "GetStringByName":return new Out(null,1,(string)call.Args[0]=="ConfigurationV1"?"Default":_json);
     default:return Unexpected(call);
    }
   });
   var feature=Com<Feature>(call=>
   {
    switch(call.MethodName)
    {
     case "get_Name":return _featureName;
     case "set_Name":_featureName=(string)call.Args[0];return null;
     case "GetTypeName2":return "MacroFeature";
     case "GetDefinition":return definition;
     case "IGetNextFeature":return null;
     case "IsSuppressed":return false;
     case "Select2":return true;
     default:return Unexpected(call);
    }
   });
   var manager=Com<FeatureManager>(call=>
   {
    if(call.MethodName!="InsertMacroFeature3")return Unexpected(call);
    _json=((string[])call.Args[5])[0];FeatureExists=true;return feature;
   });
   var configuration=Com<Configuration>(call=>call.MethodName=="get_Name"?"Default":Unexpected(call));
   var configurations=Com<ConfigurationManager>(call=>call.MethodName=="get_ActiveConfiguration"?configuration:Unexpected(call));
   var selection=Com<SelectionMgr>(call=>
   {
    if(call.MethodName=="SuspendSelectionList"){SuspendCount++;return 0;}
    if(call.MethodName=="ResumeSelectionList2"){ResumeCount++;return null;}
    return Unexpected(call);
   });
   var extension=Com<ModelDocExtension>(call=>
   {
    switch(call.MethodName)
    {
     case "IsExploded":return new Out(false,0,"");
     case "GetPersistReference3":return BitConverter.GetBytes(_versions.Single(body=>ReferenceEquals(body.Proxy,call.Args[0])).Id);
     case "GetObjectByPersistReference3":return new Out(PersistentReferenceSurvives?Bodies.Single(body=>body.Id==BitConverter.ToInt32((byte[])call.Args[0],0)).Proxy:null,1,PersistentReferenceSurvives?0:1);
     case "DeleteSelection2":
      FeatureExists=false;DeleteCount++;
      var replacement=Bodies.Select(body=>new FakeBody(body.Id,body.Name,body.Bounds) {Hidden=body.Hidden}).ToArray();
      foreach(var old in Bodies)old.Stale=true;
      Bodies.Clear();Bodies.AddRange(replacement);_versions.AddRange(replacement);return true;
     default:return Unexpected(call);
    }
   });
   var document=Com<IModelDoc2>(call=>
   {
    switch(call.MethodName)
    {
     case "get_ConfigurationManager":return configurations;
     case "get_ISelectionManager":return selection;
     case "get_FeatureManager":return manager;
     case "get_Extension":return extension;
     case "IFirstFeature":return FeatureExists?feature:null;
     case "GetConfigurationNames":return new object[]{"Default"};
     case "GetUnits":return new[]{0,0,0,3,0};
     case "GetBodies2":return Bodies.Select(body=>(object)body.Proxy).ToArray();
     case "IsOpenedReadOnly":return false;
     case "GetActiveSketch2":return null;
     case "ClearSelection2":return null;
     case "EditRebuild3":return true;
     default:return Unexpected(call);
    }
   },typeof(IPartDoc));
   Host=Com<ISldWorks>(call=>call.MethodName=="get_ActiveDoc"?document:Unexpected(call));
  }
 }
 private sealed class FakeBody
 {
  internal readonly int Id;
  internal readonly string Name;
  internal readonly double[] Bounds;
  internal readonly Body2 Proxy;
  internal bool Stale,Hidden;
  internal int StaleHideWrites;
  internal FakeBody(int id,string name,double[] bounds)
  {
   Id=id;Name=name;Bounds=(double[])bounds.Clone();
   Proxy=Com<Body2>(call=>
   {
    switch(call.MethodName)
    {
     case "get_Name":return Stale?"":Name;
     case "GetBodyBox":return Stale?null:(double[])Bounds.Clone();
     case "get_Visible":return Stale||!Hidden;
     case "HideBody":if(Stale)StaleHideWrites++;else Hidden=(bool)call.Args[0];return null;
     default:return Unexpected(call);
    }
   });
  }
 }
 private sealed class AssemblyFixture
 {
  internal readonly ISldWorks Host;
  internal readonly IModelDoc2 Document;
  internal readonly List<FakeComponent> Components=new List<FakeComponent>();
  internal readonly Dictionary<string,List<FakeStep>> Views=new Dictionary<string,List<FakeStep>>(StringComparer.Ordinal);
  internal string Expanded="",Context="",DeleteTarget="";
  internal int CreateCount,DeleteCount,ShowCount,SuspendCount,ResumeCount,ComponentReads,TransformWrites,FailAtStep,IsExpandedReads;
  internal int ViewNamesReads,SelectionManagerReads,ClearSelectionCount;
  internal object[]? FlatComponents;
  internal readonly List<bool> ComponentQueries=new List<bool>();
  internal bool FailRepeatedIsExpanded;
  internal bool RequireRebuildBeforeDelete;
  internal readonly List<string> NativeStepTrace=new List<string>();
  private readonly HashSet<string> _unrebuiltViews=new HashSet<string>(StringComparer.Ordinal);
  internal int SelectedComponentId=>_selected?.Id??0;
  private FakeComponent? _selected;
  private FakeComponent? _suspendedSelection;
  private readonly Configuration _configuration;
  private readonly ConfigurationManager _configurationManager;
  private readonly ModelDocExtension _extension;
  private readonly SelectionMgr _selection;
  private readonly SelectData _selectData;
  internal AssemblyFixture(int count=3)
  {
   _selectData=Com<SelectData>(call=>call.MethodName=="set_Mark"?null:Unexpected(call));
   _selection=Com<SelectionMgr>(call=>
   {
    switch(call.MethodName)
    {
     case "SuspendSelectionList":SuspendCount++;_suspendedSelection=_selected;return 0;
     case "ResumeSelectionList2":ResumeCount++;_selected=_suspendedSelection;return null;
     case "CreateSelectData":return _selectData;
     default:return Unexpected(call);
    }
   });
   _configuration=Com<Configuration>(call=>
   {
    switch(call.MethodName)
    {
     case "get_Name":return "Default";
     case "GetNumberOfExplodeSteps":return Views.TryGetValue(Context,out var current)?current.Count:0;
     case "GetExplodeStep":return Views[Context][(int)call.Args[0]].Proxy;
     case "DeleteExplodeStep":
      var found=Views[Context].Single(step=>step.Name==(string)call.Args[0]);Views[Context].Remove(found);return true;
     case "AddExplodeStep2":
      if(FailAtStep>0&&Views[Context].Count+1==FailAtStep)return new Out(null,8,17);
      var step=new FakeStep(_selected??throw new InvalidOperationException("Nothing selected"),(double)call.Args[0],(int)call.Args[1],(bool)call.Args[2],Views[Context].Count+1);
      Views[Context].Add(step);_unrebuiltViews.Add(Context);NativeStepTrace.Add("add:"+Context+":"+Views[Context].Count);return new Out(step.Proxy,8,0);
     default:return Unexpected(call);
    }
   });
   _configurationManager=Com<ConfigurationManager>(call=>call.MethodName=="get_ActiveConfiguration"?_configuration:Unexpected(call));
   _extension=Com<ModelDocExtension>(call=>
   {
    switch(call.MethodName)
    {
     case "IsExploded":
      IsExpandedReads++;if(FailRepeatedIsExpanded&&IsExpandedReads>1)throw new InvalidOperationException("Untouched native view must not be queried again on preflight rejection.");
      return new Out(Expanded.Length>0,0,Expanded);
     case "GetPersistReference3":return BitConverter.GetBytes(Components.Single(component=>ReferenceEquals(component.Proxy,call.Args[0])).Id);
     case "GetObjectByPersistReference3":return new Out(Components.Single(component=>component.Id==BitConverter.ToInt32((byte[])call.Args[0],0)).Proxy,1,0);
     case "IsSamePersistentID":return ((byte[])call.Args[0]).SequenceEqual((byte[])call.Args[1])?(int)swObjectEquality.swObjectSame:(int)swObjectEquality.swObjectNotSame;
     case "SelectByID2":DeleteTarget=(string)call.Args[0];return Views.ContainsKey(DeleteTarget)&&(string)call.Args[1]=="EXPLODEDVIEWS";
     case "DeleteSelection2":
      if(RequireRebuildBeforeDelete&&_unrebuiltViews.Contains(DeleteTarget))throw new InvalidOperationException("Cannot delete an unrebuilt native explode step.");
      NativeStepTrace.Add("delete:"+DeleteTarget);
      bool removed=Views.Remove(DeleteTarget);if(Expanded==DeleteTarget)Expanded="";if(removed)DeleteCount++;return removed;
     default:return Unexpected(call);
    }
   });
   Document=Com<IModelDoc2>(call=>
   {
    switch(call.MethodName)
    {
     case "get_ConfigurationManager":return _configurationManager;
     case "get_Extension":return _extension;
     case "get_ISelectionManager":SelectionManagerReads++;return _selection;
     case "IsOpenedReadOnly":return false;
     case "GetActiveSketch2":return null;
     case "GetEditTarget":return null;
     case "GetExplodedViewNames2":ViewNamesReads++;return Views.Keys.Cast<object>().ToArray();
     case "GetComponents":
      ComponentReads++;bool topLevelOnly=(bool)call.Args[0];ComponentQueries.Add(topLevelOnly);
      return !topLevelOnly&&FlatComponents!=null?FlatComponents:Components.Select(component=>(object)component.Proxy).ToArray();
     case "CreateExplodedView":
      string name="ExplView"+(++CreateCount);Views.Add(name,new List<FakeStep>());Context=name;Expanded=name;return true;
     case "ShowExploded2":
      ShowCount++;string view=(string)call.Args[1];if(!Views.ContainsKey(view))return false;
      Context=view;Expanded=(bool)call.Args[0]?view:"";return true;
     case "ClearSelection2":ClearSelectionCount++;_selected=null;return null;
     case "EditRebuild3":
      _unrebuiltViews.Remove(Context);NativeStepTrace.Add("rebuild:"+Context+":"+(Views.TryGetValue(Context,out var rebuilt)?rebuilt.Count:0));return true;
     case "ShowNamedView2":
     case "ViewZoomtofit2":
     case "GraphicsRedraw2":return null;
     default:return Unexpected(call);
    }
   },typeof(IAssemblyDoc));
   Host=Com<ISldWorks>(call=>call.MethodName=="get_ActiveDoc"?Document:Unexpected(call));
   for(int index=0;index<count;index++)Components.Add(new FakeComponent(index+1,index*.015,component=>_selected=component,()=>TransformWrites++));
  }
  internal void SeedPriorView()
  {
   Views.Add("UserPrior",new List<FakeStep>());Expanded="UserPrior";Context="UserPrior";
  }
  internal string Fingerprint(string name)=>string.Join("|",Views[name].Select(step=>step.Component.Id+"/"+step.Axis+"/"+step.Reverse+"/"+step.Distance+"/"+step.Rotation));
 }
 private sealed class FakeComponent
 {
  internal readonly int Id;
  internal readonly Component2 Proxy;
  internal bool Hidden,LightweightModelUnavailable;
  internal int SuppressedReads,ModelReads;
  internal Action? OnSuppressedRead;
  internal int Suppression=2;
  internal readonly List<int> SuppressionWrites=new List<int>();
  internal FakeComponent(int id,double x,Action<FakeComponent> selected,Action transformWritten)
  {
   Id=id;
   Proxy=Com<Component2>(call=>
   {
    switch(call.MethodName)
    {
     case "get_Name2":return "Part"+Id;
     case "IsSuppressed":SuppressedReads++;OnSuppressedRead?.Invoke();return Suppression==0;
     case "GetSuppression2":return Suppression;
     case "SetSuppression2":Suppression=(int)call.Args[0];SuppressionWrites.Add(Suppression);return (int)swSuppressionError_e.swSuppressionChangeOk;
     case "GetModelDoc2":ModelReads++;return LightweightModelUnavailable&&(Suppression==1||Suppression==4)?null:new object();
     case "GetChildren":return Array.Empty<object>();
     case "get_Visible":return Hidden?(int)swComponentVisibilityState_e.swComponentHidden:(int)swComponentVisibilityState_e.swComponentVisible;
     case "set_Visible":Hidden=(int)call.Args[0]==(int)swComponentVisibilityState_e.swComponentHidden;return null;
     case "IsHidden":return Hidden;
     case "GetBox":return new[]{x,0d,0d,x+.01,.01,.01};
     case "Select4":selected(this);return true;
     case "set_Transform2":transformWritten();throw new InvalidOperationException("Base component positions must never be written.");
     default:return Unexpected(call);
    }
   });
  }
 }
 private sealed class FakeStep
 {
  internal FakeComponent Component;
  internal readonly double Distance;
  internal readonly int Axis;
  internal bool Reverse;
  internal double Rotation;
  internal readonly string Name;
  internal readonly ExplodeStep Proxy;
  internal FakeStep(FakeComponent component,double distance,int axis,bool reverse,int number)
  {
   Component=component;Distance=distance;Axis=axis;Reverse=reverse;Name="Step"+number;
   Proxy=Com<ExplodeStep>(call=>
   {
    switch(call.MethodName)
    {
     case "get_Name":return Name;
     case "get_RotationAngle":return Rotation;
     case "get_DivergeFromAxis":
     case "get_AutoSpaceComponentsOnDrag":return false;
     case "GetComponents":return new object[]{Component.Proxy};
     case "GetExplodeDirection":return new Out(null,0,Axis);
     case "get_ExplodeDistance":return Distance;
     case "get_ReverseTranslationDirection":return Reverse;
     case "set_ReverseTranslationDirection":Reverse=(bool)call.Args[0];return null;
     default:return Unexpected(call);
    }
   });
  }
 }
 private sealed class Out
 {
  internal readonly object? Result,Value;
  internal readonly int Index;
  internal Out(object? result,int index,object? value){Result=result;Index=index;Value=value;}
 }
 private static object? Unexpected(IMethodCallMessage call)=>throw new InvalidOperationException("Unexpected native call: "+call.MethodBase.DeclaringType+"."+call.MethodName);
 private static T Com<T>(Func<IMethodCallMessage,object?> action,params Type[] extraInterfaces)=>(T)new FakeCom(typeof(T),action,extraInterfaces).GetTransparentProxy();
 private sealed class FakeCom:RealProxy,IRemotingTypeInfo
 {
  private readonly Type _type;private readonly Type[] _extra;private readonly Func<IMethodCallMessage,object?> _action;
  internal FakeCom(Type type,Func<IMethodCallMessage,object?> action,Type[] extra):base(type){_type=type;_action=action;_extra=extra;}
  public string TypeName {get=>_type.AssemblyQualifiedName!;set{}}
  public bool CanCastTo(Type fromType,object obj)=>fromType==typeof(object)||fromType.IsAssignableFrom(_type)||_extra.Any(type=>fromType.IsAssignableFrom(type));
  public override IMessage Invoke(IMessage message)
  {
   var call=(IMethodCallMessage)message;
   try
   {
    object? result=call.MethodName=="GetType"?_type:call.MethodName=="GetHashCode"?RuntimeHelpers.GetHashCode(this):call.MethodName=="Equals"?ReferenceEquals(GetTransparentProxy(),call.Args[0]):_action(call);
    var args=call.Args;
    if(result is Out response){args[response.Index]=response.Value;result=response.Result;}
    return new ReturnMessage(result,args,args.Length,call.LogicalCallContext,call);
   }
   catch(Exception error){return new ReturnMessage(error,call);}
  }
 }
}

