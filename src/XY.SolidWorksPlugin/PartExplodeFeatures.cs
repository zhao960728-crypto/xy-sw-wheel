using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

// Suppressing the one owned feature restores its native upstream bodies.
// Definitions in new models are isolated by their owning configuration.
internal sealed class PartExplodeFeatures
{
 internal static string CallbackProgId = ExplodeMacroFeature.ProgramId;
 private readonly IModelDoc2 _document;
 internal bool LastCreateWasReused { get; private set; }
 public PartExplodeFeatures(IModelDoc2 document) { _document=document??throw new ArgumentNullException(nameof(document)); }
 private IEnumerable<IFeature> Features()
 {
  string configuration=_document.ConfigurationManager.ActiveConfiguration.Name;
  for(var feature=_document.IFirstFeature();feature!=null;feature=feature.IGetNextFeature())
  {
   if(feature.GetTypeName2()!="MacroFeature"||!(feature.GetDefinition() is IMacroFeatureData data)||data.GetBaseName()!=ExplodeMacroFeature.BaseName)continue;
   string owner="";
   try { data.GetStringByName(ExplodeMacroFeature.ConfigurationParameterName,out owner); }
   catch { /* Legacy V1 features did not record an owning configuration. */ }
   if(string.IsNullOrEmpty(owner)||owner==configuration)yield return feature;
  }
 }
 public string[] Views()=>Features().Select(feature=>feature.Name).ToArray();
 private IFeature Find(string name)=>Features().SingleOrDefault(feature=>feature.Name==name)??throw new InvalidOperationException("一键爆炸特征不存在或已删除。");
 public string ExpandedName()=>Features().FirstOrDefault(feature=>!feature.IsSuppressed())?.Name??"";
 public void Show(string name,bool expanded)
 {
  var feature=Find(name);
  if(expanded)foreach(var other in Features().Where(item=>item.Name!=name&&!item.IsSuppressed()).ToArray())SetSuppressed(other,true);
  if(!expanded||!feature.IsSuppressed()){SetSuppressed(feature,!expanded);return;}
  ((IMacroFeatureData)feature.GetDefinition()).GetStringByName(ExplodeMacroFeature.ParameterName,out string json);
  var moves=ExplodeMacroFeature.Parse(json);var expected=ExpectedBounds(BodyBounds(),moves);
  try {SetSuppressed(feature,false);VerifyOutputIdentities(Find(name),expected,moves.Select(move=>move.Name).ToArray(),null);}
  catch(Exception error)
  {
   try {SetSuppressed(Find(name),true);}
   catch(Exception rollback){throw new InvalidOperationException("展开爆炸失败，收拢恢复未完成："+rollback.Message,error);}
   throw;
  }
 }
 private void SetSuppressed(IFeature feature,bool suppressed)
 {
  string name=feature.Name;
  if(feature.IsSuppressed()!=suppressed)
  {
   _document.ClearSelection2(true);
   if(!feature.SetSuppression2((int)(suppressed?swFeatureSuppressionAction_e.swSuppressFeature:swFeatureSuppressionAction_e.swUnSuppressFeature),(int)swInConfigurationOpts_e.swThisConfiguration,null)||!_document.EditRebuild3()||Find(name).IsSuppressed()!=suppressed)
    throw new InvalidOperationException("一键爆炸特征状态切换失败，请检查特征树。");
  }
  if(!suppressed&&Find(name).GetErrorCode2(out bool warning)!=0&&!warning)
  {
   SetSuppressed(Find(name),true);
   throw new InvalidOperationException("一键爆炸特征重建出错，已收拢复原。");
  }
 }
 public void Delete(string name)
 {
  var feature=Find(name);_document.ClearSelection2(true);
  if(!feature.Select2(false,0)||!_document.Extension.DeleteSelection2(0)||!_document.EditRebuild3()||Views().Contains(name))
   throw new InvalidOperationException("一键爆炸特征删除未完成，请检查特征树。");
 }
 public string Create(ExplodeMove[] moves,CancellationToken cancellation=default,Action validate=null,bool preserveSelection=true)
 {
  void Check() { cancellation.ThrowIfCancellationRequested();validate?.Invoke(); }
  Check();LastCreateWasReused=false;
  if(moves==null||moves.Length==0||moves.Length>1000||moves.Any(move=>move?.Target==null||!(move.Target.Entity is IBody2)||move.Axis<0||move.Axis>2||move.Distance<=0||double.IsNaN(move.Distance)||double.IsInfinity(move.Distance)||move.Target.Bounds==null||move.Target.Bounds.Length!=6))
   throw new InvalidOperationException("爆炸移动参数无效，未修改模型。");
  var bodies=moves.Select(move=>(IBody2)move.Target.Entity).ToArray();
  if(moves.Where((move,index)=>!SameBox(bodies[index].GetBodyBox() as double[],move.Target.Bounds)).Any())
   throw new InvalidOperationException("模型实体已变化，未修改爆炸特征。");
  var parameters=moves.Select((move,index)=>{var delta=new double[3];delta[move.Axis]=(move.Reverse?-1:1)*move.Distance;return new ExplodeMacroFeature.Move {Name=bodies[index].Name,Delta=delta};}).ToArray();
  string json=new JavaScriptSerializer().Serialize(parameters);ExplodeMacroFeature.Parse(json);
  var expected=ExpectedBounds(BodyBounds(),parameters);
  var existing=Features().ToArray();
  if(existing.Length>1)throw new InvalidOperationException("当前配置存在多个旧爆炸特征，请先压缩并整理为一个。");
  if(existing.Length==1)return Update(existing[0],json,expected,Check);
  string created="";IFeature createdFeature=null;
  var selection=_document.ISelectionManager;if(preserveSelection)selection.SuspendSelectionList();
  try
  {
   Check();_document.ClearSelection2(true);
   string configuration=_document.ConfigurationManager.ActiveConfiguration.Name;
   createdFeature=_document.FeatureManager.InsertMacroFeature3(ExplodeMacroFeature.BaseName,CallbackProgId,null,
    new[]{ExplodeMacroFeature.ParameterName,ExplodeMacroFeature.ConfigurationParameterName},
    new[]{(int)swMacroFeatureParamType_e.swMacroFeatureParamTypeString,(int)swMacroFeatureParamType_e.swMacroFeatureParamTypeString},
    new[]{json,configuration},null,null,bodies.Select(body=>new System.Runtime.InteropServices.DispatchWrapper(body)).ToArray(),null,(int)swMacroFeatureOptions_e.swMacroFeatureByDefault);
   if(createdFeature==null)throw new InvalidOperationException("无法创建一键爆炸特征，请确认插件回调已注册。");
   created=createdFeature.Name;
   createdFeature.Name="XY一键爆炸";created=createdFeature.Name;
   var others=(_document.GetConfigurationNames() as object[]??Array.Empty<object>()).Select(Convert.ToString).Where(name=>name!=configuration).ToArray();
   if(others.Length>0&&!createdFeature.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,(int)swInConfigurationOpts_e.swSpecifyConfiguration,others))
    throw new InvalidOperationException("无法保留其他配置的原始位置。");
   Check();
   if(!_document.EditRebuild3())throw new InvalidOperationException("一键爆炸特征重建失败。");
   Check();VerifyFeatureError(createdFeature);VerifyOutputIdentities(createdFeature,expected,parameters.Select(move=>move.Name).ToArray(),Check);
   return created;
  }
  catch(Exception error)
  {
   try
   {
    if(createdFeature!=null)
    {
     if(string.IsNullOrEmpty(created))created=createdFeature.Name;
     Delete(created);
    }
   }
   catch(Exception cleanup){throw new InvalidOperationException("爆炸未完成，新增特征清理失败："+cleanup.Message,error);}
   if(error is OperationCanceledException)throw;
   throw new InvalidOperationException("爆炸未完成，已移除本次未完成特征："+error.Message,error);
  }
  finally {if(preserveSelection)try{_document.ClearSelection2(true);}finally{selection.ResumeSelectionList2(false);}}
 }
 private IBody2[] CurrentBodies()=>(((IPartDoc)_document).GetBodies2((int)swBodyType_e.swSolidBody,false) as object[]??Array.Empty<object>()).OfType<IBody2>().ToArray();
 private Dictionary<string,double[]> BodyBounds()=>BodyBounds(CurrentBodies());
 private static Dictionary<string,double[]> BodyBounds(IEnumerable<IBody2> bodies)
 {
  var result=new Dictionary<string,double[]>(StringComparer.Ordinal);
  foreach(var body in bodies)
  {
   string name=body.Name;var bounds=body.GetBodyBox() as double[];
   if(string.IsNullOrEmpty(name)||result.ContainsKey(name)||bounds==null||bounds.Length!=6||bounds.Any(value=>double.IsNaN(value)||double.IsInfinity(value)))
    throw new InvalidOperationException("实体名称或边界不明确，未修改爆炸特征。");
   result.Add(name,bounds);
  }
  return result;
 }
 private static Dictionary<string,double[]> ExpectedBounds(IReadOnlyDictionary<string,double[]> original,IEnumerable<ExplodeMacroFeature.Move> moves)
 {
  var result=original.ToDictionary(pair=>pair.Key,pair=>(double[])pair.Value.Clone(),StringComparer.Ordinal);
  foreach(var move in moves)
  {
   if(!result.TryGetValue(move.Name,out var bounds))throw new InvalidOperationException("爆炸输入实体已变化，未修改模型："+move.Name);
   result[move.Name]=bounds.Select((value,axis)=>value+move.Delta[axis%3]).ToArray();
  }
  return result;
 }
 // SOLIDWORKS assigns feature-owned names to modified output bodies. Accept only
 // this exact feature's generated names, and prove the whole geometry bijection
 // before projecting output labels; an unchanged anchor can never be substituted.
 internal static IReadOnlyDictionary<string,string> PlanOutputNameRestoration(string featureName,IReadOnlyCollection<string> movedNames,IReadOnlyDictionary<string,double[]> expected,IReadOnlyDictionary<string,double[]> actual)
 {
  InvalidOperationException Rejected()=>new InvalidOperationException("爆炸输出实体身份与几何不能唯一对应。 Expected="+DescribeBounds(expected)+"; Actual="+DescribeBounds(actual));
  if(string.IsNullOrEmpty(featureName)||expected.Count!=actual.Count)throw Rejected();
  var moved=new HashSet<string>(movedNames,StringComparer.Ordinal);
  var remaining=actual.Where(pair=>!expected.ContainsKey(pair.Key)).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
  foreach(var pair in actual.Where(pair=>expected.ContainsKey(pair.Key)))if(!SameBox(pair.Value,expected[pair.Key]))throw Rejected();
  if(remaining.Keys.Any(name=>!IsGeneratedOutputName(featureName,name)))throw Rejected();
  var renames=new Dictionary<string,string>(StringComparer.Ordinal);
  foreach(var pair in expected.Where(pair=>!actual.ContainsKey(pair.Key)))
  {
   if(!moved.Contains(pair.Key))throw Rejected();
   var matches=remaining.Where(candidate=>SameBox(candidate.Value,pair.Value)).Select(candidate=>candidate.Key).ToArray();
   if(matches.Length!=1)throw Rejected();
   renames.Add(matches[0],pair.Key);remaining.Remove(matches[0]);
  }
  if(remaining.Count!=0)throw Rejected();
  return renames;
 }
 private static bool IsGeneratedOutputName(string featureName,string name)
 {
  if(name==featureName)return true;
  string prefix=featureName+"[";
  if(!name.StartsWith(prefix,StringComparison.Ordinal)||!name.EndsWith("]",StringComparison.Ordinal))return false;
  string suffix=name.Substring(prefix.Length,name.Length-prefix.Length-1);
  return int.TryParse(suffix,NumberStyles.None,CultureInfo.InvariantCulture,out int index)&&index>0&&suffix==index.ToString(CultureInfo.InvariantCulture);
 }
 internal static void VerifyOutputIdentities(string featureName,IReadOnlyCollection<string> movedNames,IReadOnlyDictionary<string,double[]> expected,IReadOnlyDictionary<string,double[]> actual)
 {
  var identities=PlanOutputNameRestoration(featureName,movedNames,expected,actual);
  VerifyBounds(expected,actual.ToDictionary(pair=>identities.TryGetValue(pair.Key,out string original)?original:pair.Key,pair=>pair.Value,StringComparer.Ordinal));
 }
 private void VerifyOutputIdentities(IFeature feature,IReadOnlyDictionary<string,double[]> expected,IReadOnlyCollection<string> movedNames,Action check)
 {
  check?.Invoke();var actual=BodyBounds();
  // Native macro output labels are read-only in some host versions. The proven
  // bijection supplies logical identity without writing any body or geometry.
  VerifyOutputIdentities(feature.Name,movedNames,expected,actual);check?.Invoke();
 }
 internal static void VerifyBounds(IReadOnlyDictionary<string,double[]> expected,IReadOnlyDictionary<string,double[]> actual)
 {
  if(expected.Count!=actual.Count||expected.Any(pair=>!actual.TryGetValue(pair.Key,out var bounds)||!SameBox(bounds,pair.Value)))
  {
   string detail="ExpectedCount="+expected.Count+"; ActualCount="+actual.Count+"; Expected="+DescribeBounds(expected)+"; Actual="+DescribeBounds(actual);
   Logger.Error("Part explode geometry verification failed. "+detail);
   throw new InvalidOperationException("实体实际位置或数量与爆炸计划不一致。 "+detail);
  }
 }
 private static string DescribeBounds(IReadOnlyDictionary<string,double[]> bodies)=>string.Join(" | ",bodies.Take(8).Select(pair=>pair.Key+"=["+FormatBounds(pair.Value)+"]"))+(bodies.Count>8?" | ...":"");
 internal static string FormatBounds(double[] bounds)=>bounds==null?"null":string.Join(",",bounds.Select(value=>value.ToString("G17",CultureInfo.InvariantCulture)));
 private static void VerifyFeatureError(IFeature feature)
 {
  int error=feature.GetErrorCode2(out bool warning);
  if(error!=0&&!warning)throw new InvalidOperationException("一键爆炸宏特征重建被宿主拒绝，未接受几何结果。 FeatureError="+error+"; Warning="+warning+"; Feature="+feature.Name);
 }
 internal static bool SameBox(double[] actual,double[] expected)=>actual!=null&&expected!=null&&actual.Length==6&&expected.Length==6&&actual.Zip(expected,(left,right)=>!double.IsNaN(left)&&!double.IsInfinity(left)&&Math.Abs(left-right)<1e-6).All(equal=>equal);
 internal static bool SameParameters(string first,string second)
 {
  var before=ExplodeMacroFeature.Parse(first).ToDictionary(move=>move.Name,StringComparer.Ordinal);var after=ExplodeMacroFeature.Parse(second);
  return before.Count==after.Length&&after.All(move=>before.TryGetValue(move.Name,out var old)&&old.Delta.Zip(move.Delta,(left,right)=>Math.Abs(left-right)<=1e-10).All(equal=>equal));
 }
 internal static void ValidateDefinitionUpdate(string owner,string configuration,IReadOnlyCollection<string> configurations,bool identical)
 {
  if(!string.IsNullOrEmpty(owner)&&owner!=configuration)
   throw new InvalidOperationException("该爆炸特征属于其他配置，未修改其保存参数。");
  if(!identical&&string.IsNullOrEmpty(owner)&&configurations.Distinct(StringComparer.Ordinal).Count()>1)
   throw new InvalidOperationException("旧版爆炸特征未记录所属配置。为保护其他配置，已保留其原参数；请在原配置中整理旧特征后再运行一键爆炸。");
 }
 private void ChangeDefinition(IFeature feature,string json)
 {
  var moves=ExplodeMacroFeature.Parse(json);SetSuppressed(feature,false);
  var data=(IMacroFeatureData)Find(feature.Name).GetDefinition();
  if(!data.AccessSelections(_document,null))throw new InvalidOperationException("无法访问爆炸特征输入。");
  bool committed=false;
  try
  {
   var inputs=(((IPartDoc)_document).GetBodies2((int)swBodyType_e.swSolidBody,false) as object[]??Array.Empty<object>()).OfType<IBody2>().ToDictionary(body=>body.Name,StringComparer.Ordinal);
   var selected=moves.Select(move=>inputs.TryGetValue(move.Name,out var body)?body:throw new InvalidOperationException("爆炸输入实体已变化："+move.Name)).ToArray();
   data.EditBodies=selected.Select(body=>new System.Runtime.InteropServices.DispatchWrapper(body)).ToArray();
   data.SetStringByName(ExplodeMacroFeature.ParameterName,json);committed=feature.ModifyDefinition(data,_document,null);
   if(!committed)throw new InvalidOperationException("宿主未接受爆炸参数更新。");
  }
  finally {if(!committed)data.ReleaseSelectionAccess();}
  if(!_document.EditRebuild3())throw new InvalidOperationException("爆炸参数更新后重建失败。");
 }
 private string Update(IFeature feature,string json,IReadOnlyDictionary<string,double[]> expected,Action check)
 {
  string name=feature.Name;
  if(!feature.IsSuppressed())throw new InvalidOperationException("请先收拢爆炸特征。");
  ((IMacroFeatureData)feature.GetDefinition()).GetStringByName(ExplodeMacroFeature.ParameterName,out string previous);
  bool identical=SameParameters(previous,json);
  string owner="";
  try {((IMacroFeatureData)feature.GetDefinition()).GetStringByName(ExplodeMacroFeature.ConfigurationParameterName,out owner);}catch{}
  var configurations=(_document.GetConfigurationNames() as object[]??Array.Empty<object>()).Select(Convert.ToString).ToArray();
  ValidateDefinitionUpdate(owner,_document.ConfigurationManager.ActiveConfiguration.Name,configurations,identical);
  try
  {
   check();if(identical)SetSuppressed(feature,false);else ChangeDefinition(feature,json);
   check();VerifyFeatureError(Find(name));VerifyOutputIdentities(Find(name),expected,ExplodeMacroFeature.Parse(json).Select(move=>move.Name).ToArray(),check);LastCreateWasReused=identical;return name;
  }
  catch(Exception error)
  {
   try {if(!identical)ChangeDefinition(Find(name),previous);SetSuppressed(Find(name),true);}
   catch(Exception rollback){throw new InvalidOperationException("更新爆炸失败，原参数恢复未完成："+rollback.Message,error);}
   if(error is OperationCanceledException)throw;
   throw new InvalidOperationException("更新爆炸失败，已恢复原参数并收拢："+error.Message,error);
  }
 }
}

