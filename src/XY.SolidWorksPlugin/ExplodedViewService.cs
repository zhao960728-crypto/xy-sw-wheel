using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

// All calls stay on the caller's host thread. Assembly moves are native explode
// steps, never component Transform2 writes or mate edits.
internal sealed class ExplodedViewService
{
 private readonly ISldWorks _host;
 private readonly IModelDoc2 _document;
 private readonly string _configuration;
 private readonly int _ownerThread;
 private readonly PartExplodeFeatures _partFeatures;
 internal sealed class BodyVisibilityIdentity
 {
  internal readonly string Name;
  internal readonly double[] Bounds;
  internal readonly byte[] Reference;
  internal BodyVisibilityIdentity(string name,double[] bounds,byte[] reference)
  {
   if(string.IsNullOrWhiteSpace(name)||bounds==null||bounds.Length!=6||bounds.Any(value=>double.IsNaN(value)||double.IsInfinity(value)))
    throw new InvalidOperationException("无法完整记录隐藏实体身份，未创建爆炸特征。");
   Name=name;Bounds=(double[])bounds.Clone();Reference=reference==null?null:(byte[])reference.Clone();
  }
 }
 private sealed class VisibilityChange
 {
  internal object Component;
  internal BodyVisibilityIdentity Body;
 }
 public string LastAutomaticSummary { get; private set; }="";
 public string LastCreatedView { get; private set; }="";
 public bool LastAutomaticWasReused { get; private set; }
 public ExplodedViewService(ISldWorks host)
 {
  _host=host;_document=host?.ActiveDoc as IModelDoc2;
  if(_document==null||!(_document is IPartDoc||_document is IAssemblyDoc))throw new InvalidOperationException("请打开多实体零件或装配体。");
  _configuration=_document.ConfigurationManager.ActiveConfiguration.Name;
  _ownerThread=Thread.CurrentThread.ManagedThreadId;
  if(_document is IPartDoc)_partFeatures=new PartExplodeFeatures(_document);
 }
 private void Validate()
 {
  if(Thread.CurrentThread.ManagedThreadId!=_ownerThread)throw new InvalidOperationException("一键爆炸必须在启动它的 SOLIDWORKS 界面线程执行。");
  if(!Equals(_host.ActiveDoc,_document)||_document.ConfigurationManager.ActiveConfiguration.Name!=_configuration)
   throw new InvalidOperationException("模型或配置已切换，请重新运行一键爆炸。");
  if(_document.IsOpenedReadOnly()||_document.GetActiveSketch2()!=null)throw new InvalidOperationException("请退出草图编辑，并确认当前文档可写。");
  if(_document is IAssemblyDoc assembly&&assembly.GetEditTarget() is IModelDoc2 target&&!Equals(target,_document))
   throw new InvalidOperationException("请先退出装配体中的零部件编辑。");
 }
 public string[] Views()
 {
  Validate();
  if(_partFeatures!=null)return _partFeatures.Views();
  return (((IAssemblyDoc)_document).GetExplodedViewNames2(_configuration) as object[]??Array.Empty<object>()).Select(Convert.ToString).Where(name=>!string.IsNullOrEmpty(name)).ToArray();
 }
 public bool IsExpanded(out string name)
 {
  Validate();name=_partFeatures?.ExpandedName()??"";
  return name.Length>0||_document.Extension.IsExploded(out name);
 }
 public void Show(string name,bool expanded)
 {
  Validate();var selection=_document.ISelectionManager;selection.SuspendSelectionList();
  try { SetExpanded(name,expanded);TryOrient(expanded); }
  finally { RestoreSelection(selection); }
 }
 private void SetExpanded(string name,bool expanded)
 {
  Validate();
  if(_partFeatures!=null)_partFeatures.Show(name,expanded);
  else if(!Views().Contains(name)||!((IAssemblyDoc)_document).ShowExploded2(expanded,name))
   throw new InvalidOperationException("SOLIDWORKS 未能切换爆炸视图。");
 }
 public void OrientIsometric() { Validate();TryOrient(true); }
 private void TryOrient(bool isometric)
 {
  try
  {
   if(isometric)_document.ShowNamedView2("",(int)swStandardViews_e.swIsometricView);
   _document.ViewZoomtofit2();_document.GraphicsRedraw2();
  }
  catch(Exception error){Logger.Error("Explode completed; viewport refresh failed",error);}
 }
 public void DeletePartGroup(string name)
 {
  Validate();
  if(_partFeatures==null)throw new InvalidOperationException("装配体请在配置管理器管理原生爆炸视图。");
  var selection=_document.ISelectionManager;selection.SuspendSelectionList();
  try {_partFeatures.Delete(name);TryOrient(false);}
  finally {RestoreSelection(selection);}
 }
 private void RestoreSelection(ISelectionMgr selection)
 {
  bool trace=string.Equals(System.Environment.GetEnvironmentVariable("XY_EXPLODE_TRACE"),"1",StringComparison.Ordinal);
  try
  {
   TraceRestore(trace,"RestoreSelection.Clear","begin");_document.ClearSelection2(true);TraceRestore(trace,"RestoreSelection.Clear","end");
  }
  finally
  {
   TraceRestore(trace,"RestoreSelection.Resume","begin");selection.ResumeSelectionList2(false);TraceRestore(trace,"RestoreSelection.Resume","end");
  }
 }
 private static void TraceRestore(bool enabled,string operation,string phase,string value="")
 {
  if(enabled)Logger.Info("Explode state recovery. Operation="+operation+"; Phase="+phase+(value.Length==0?"":"; Value="+value));
 }
 public int DocumentStamp() {Validate();return _document.GetUpdateStamp();}
 public string CreateAllAutomatic(CancellationToken cancellation=default,Action<string> progress=null)
 {
  void Check(string message=null)
  {
   cancellation.ThrowIfCancellationRequested();Validate();
   if(message!=null)progress?.Invoke(message);
   cancellation.ThrowIfCancellationRequested();Validate();
  }
  // A pre-cancelled command does not even collapse the current view or touch selection.
  Check("正在检查一键爆炸范围…");
  LastAutomaticSummary="";LastCreatedView="";LastAutomaticWasReused=false;
  RejectSuppressedAssemblyBeforeViewTransaction(()=>Check());
  var hidden=new List<object>();var changedVisibility=new List<VisibilityChange>();
  var resolved=new List<Tuple<IComponent2,int>>();var issues=new List<string>();
  string prior="";bool priorExpanded=IsExpanded(out prior),viewStateTouched=false;
  bool trace=string.Equals(System.Environment.GetEnvironmentVariable("XY_EXPLODE_TRACE"),"1",StringComparison.Ordinal);
  var selection=_document.ISelectionManager;selection.SuspendSelectionList();
  try
  {
   if(priorExpanded){viewStateTouched=true;SetExpanded(prior,false);}
   Check("正在读取全部实体/零部件…");
   var targets=new List<object>();
   if(_document is IPartDoc part)
    targets.AddRange(part.GetBodies2((int)swBodyType_e.swSolidBody,false) as object[]??Array.Empty<object>());
   else
    ReadAssemblyTargets(((IAssemblyDoc)_document).GetComponents(true) as object[],targets,hidden,resolved,issues,()=>Check("正在读取全部零部件…"));
   if(targets.Count>1000)throw new InvalidOperationException("全部范围超过 1000 个对象，未生成部分爆炸图。");
   if(issues.Count>0)throw IncompleteScope(issues,issues.Count);
   if(targets.Count<2)throw new InvalidOperationException(_partFeatures!=null?"当前零件不足两个独立实体，无法拆开；若要散开全部零部件，请打开所属装配体。":"当前装配体不足两个可加载的末级零部件。");
   var geometry=new List<ExplodeTarget>();
   foreach(var item in targets)
   {
    if((geometry.Count&31)==0)Check("正在读取对象边界 "+geometry.Count+"/"+targets.Count+"…");
    if(item is IBody2 body)geometry.Add(new ExplodeTarget {Entity=body,Name=body.Name,Bounds=body.GetBodyBox() as double[]});
    else if(item is IComponent2 component)geometry.Add(new ExplodeTarget {Entity=component,Name=component.Name2,Bounds=component.GetBox(false,false) as double[]});
    else throw new InvalidOperationException("宿主返回了无法识别的爆炸对象，未修改模型。");
   }
   var ordered=geometry.OrderBy(target=>target.Name,StringComparer.OrdinalIgnoreCase).ThenBy(target=>target.Name,StringComparer.Ordinal).ToArray();
   Check("正在规划互不遮挡的爆炸位置…");
   // Validate geometry and plan once; the old path ran the quadratic planner twice.
   var plan=ExplodedViewPlanner.PlanAutomatic(ordered,DistanceQuantum(),cancellation,out double gap);
   var moves=plan.Where(move=>move.Distance>0).ToArray();
   Check("正在准备爆炸视图…");
   foreach(var body in targets.OfType<IBody2>().Where(body=>!body.Visible))hidden.Add(body);
   foreach(var item in hidden.Distinct())
   {
    Check();changedVisibility.Add(CaptureVisibilityChange(item));SetVisible(item,true);
   }
   string created;viewStateTouched=true;
   if(_partFeatures!=null)
   {
    created=_partFeatures.Create(moves,cancellation,()=>Check("正在验证一键爆炸实体…"),false);
    LastAutomaticWasReused=_partFeatures.LastCreateWasReused;
   }
   else
    created=CreateOrReuseAssembly(moves,cancellation,Check);
   // Nothing cancellable runs after this commit. A successful reused view is
   // never deleted because Escape happened while displaying the success message.
   LastCreatedView=created;
   LastAutomaticSummary="全部 "+targets.Count+" 个对象，"+moves.Length+" 个移动步骤；自动净间距 "+(gap*1000).ToString("0.#")+" mm。"
    +(LastAutomaticWasReused?"已复用现有爆炸视图。":"")
    +(hidden.Count>0?"已显示 "+hidden.Distinct().Count()+" 个原隐藏对象。":"")+"保存模型即可保留。";
   TryOrient(true);
   return created;
  }
  catch(Exception error)
  {
   var failures=new List<string>();
   IBody2[] currentBodies=null;
   if(changedVisibility.Any(change=>change.Body!=null))
    try {currentBodies=CurrentBodies();}catch(Exception failure){failures.Add(failure.Message);}
   foreach(var change in changedVisibility.AsEnumerable().Reverse())
    try
    {
     object item=change.Body==null?change.Component:ResolveVisibilityBody(change.Body,currentBodies,reference=>
     {
      var body=_document.Extension.GetObjectByPersistReference3(reference,out int state) as IBody2;
      return state==0?body:null;
     });
     SetVisible(item,false);
    }
    catch(Exception failure){failures.Add(failure.Message);}
   try
   {
    if(viewStateTouched)
    {
     TraceRestore(trace,"Catch.IsExpanded","begin");bool expanded=IsExpanded(out string active);TraceRestore(trace,"Catch.IsExpanded","end",expanded.ToString());
     if(expanded){TraceRestore(trace,"Catch.SetExpanded.collapse","begin");SetExpanded(active,false);TraceRestore(trace,"Catch.SetExpanded.collapse","end");}
     if(priorExpanded){TraceRestore(trace,"Catch.SetExpanded.prior","begin");SetExpanded(prior,true);TraceRestore(trace,"Catch.SetExpanded.prior","end");}
    }
    else TraceRestore(trace,"Catch.ViewState","untouched-skip");
   }
   catch(Exception failure){failures.Add(failure.Message);}
   foreach(var item in resolved.AsEnumerable().Reverse())
    try
    {
     item.Item1.SetSuppression2(item.Item2);
     if(item.Item1.GetSuppression2()!=item.Item2)throw new InvalidOperationException("轻量化状态未恢复："+item.Item1.Name2);
    }
    catch(Exception failure){failures.Add(failure.Message);}
   if(failures.Count>0)throw new InvalidOperationException(error.Message+" 原状态恢复未完成："+string.Join("；",failures),error);
   throw;
  }
  finally {RestoreSelection(selection);}
 }
 private void RejectSuppressedAssemblyBeforeViewTransaction(Action check)
 {
  if(!(_document is IAssemblyDoc assembly))return;
  // A rejected suppressed assembly must not suspend/clear/resume selection:
  // that native round-trip can corrupt the document's later close operation.
  // False includes all hierarchy levels; this is only a rejection check, not
  // a replacement for ReadAssemblyTargets' completeness and resolution checks.
  var components=assembly.GetComponents(false) as object[]??Array.Empty<object>();
  if(components.Length>10000)throw new InvalidOperationException("装配体层级范围过大，请精简到 1000 个末级对象后重试。");
  var issues=new List<string>(8);int suppressed=0;
  for(int index=0;index<components.Length;index++)
  {
   if((index&31)==0)check();
   if(components[index] is IComponent2 component&&component.IsSuppressed())
   {
    suppressed++;
    if(issues.Count<8)issues.Add("已抑制："+component.Name2);
   }
  }
  check();
  if(suppressed>0)throw IncompleteScope(issues,suppressed);
 }
 private static InvalidOperationException IncompleteScope(IReadOnlyList<string> issues,int count)=>
  new InvalidOperationException("无法完整爆炸全部对象，请先处理："+string.Join("；",issues.Take(8))
   +(count>8?"；另有 "+(count-8)+" 项":"")+"。未生成部分爆炸图。");
 private IBody2[] CurrentBodies()=>(((IPartDoc)_document).GetBodies2((int)swBodyType_e.swSolidBody,false) as object[]??Array.Empty<object>()).OfType<IBody2>().ToArray();
 private VisibilityChange CaptureVisibilityChange(object item)
 {
  if(!(item is IBody2 body))return new VisibilityChange {Component=item};
  byte[] reference=null;
  try {reference=_document.Extension.GetPersistReference3(body) as byte[];}
  catch(Exception error){Logger.Trace("Body visibility persistent identity unavailable; requiring unique current name and bounds: "+error.Message);}
  return new VisibilityChange {Body=new BodyVisibilityIdentity(body.Name,body.GetBodyBox() as double[],reference)};
 }
 internal static IBody2 ResolveVisibilityBody(BodyVisibilityIdentity identity,IReadOnlyList<IBody2> currentBodies,Func<byte[],IBody2> resolve)
 {
  if(identity==null||currentBodies==null)throw new InvalidOperationException("无法重新读取隐藏实体，显示状态恢复未完成。");
  IBody2 persistent=null;
  if(identity.Reference?.Length>0)
   try {persistent=resolve(identity.Reference);}catch(Exception error){Logger.Trace("Body visibility persistent lookup failed; validating fresh body identity: "+error.Message);}
  // Never call HideBody on a saved RCW: macro insertion/deletion can replace
  // that native body while leaving its former managed wrapper alive.
  if(persistent!=null)
  {
   var current=currentBodies.SingleOrDefault(body=>Equals(body,persistent));
   if(current!=null&&current.Name==identity.Name&&PartExplodeFeatures.SameBox(current.GetBodyBox() as double[],identity.Bounds))return current;
  }
  var named=currentBodies.Where(body=>body.Name==identity.Name).Take(2).ToArray();
  if(named.Length!=1||!PartExplodeFeatures.SameBox(named[0].GetBodyBox() as double[],identity.Bounds))
   throw new InvalidOperationException("隐藏实体无法按唯一身份和原始边界重新确认，显示状态恢复未完成："+identity.Name);
  return named[0];
 }
 private void ReadAssemblyTargets(object[] roots,List<object> targets,List<object> hidden,List<Tuple<IComponent2,int>> resolved,List<string> issues,Action check)
 {
  bool trace=string.Equals(System.Environment.GetEnvironmentVariable("XY_EXPLODE_TRACE"),"1",StringComparison.Ordinal);
  int item=0;
  void Trace(string operation,string phase,string value="")
  {
   if(trace)Logger.Info("Explode assembly scope. Item="+item+"; Operation="+operation+"; Phase="+phase+(value.Length==0?"":"; Value="+value));
  }
  T Read<T>(string operation,Func<T> read,Func<T,string> describe=null)
  {
   Trace(operation,"begin");
   try {T value=read();Trace(operation,"end",describe==null?"":describe(value));return value;}
   catch(Exception error){Trace(operation,"throw",error.GetType().Name+":"+error.Message);throw;}
  }
  Trace("tree","begin","Roots="+(roots?.Length??0));
  var pending=new Stack<IComponent2>((roots??Array.Empty<object>()).OfType<IComponent2>().Reverse());
  var visited=new HashSet<object>();int inspected=0;
  while(pending.Count>0)
  {
   if((inspected++&31)==0)check();
   if(inspected>10000)throw new InvalidOperationException("装配体层级范围过大，请精简到 1000 个末级对象后重试。");
   var component=pending.Pop();
   item=inspected;
   if(!(trace?Read("visited.Add",()=>visited.Add(component),value=>value.ToString()):visited.Add(component)))throw new InvalidOperationException("装配体层级存在重复对象，未创建部分爆炸图。");
   if(trace?Read("IsSuppressed",()=>component.IsSuppressed(),value=>value.ToString()):component.IsSuppressed()){issues.Add("已抑制："+(trace?Read("Name2.suppressed",()=>component.Name2,value=>value??"<null>"):component.Name2));continue;}
   var model=trace?Read("GetModelDoc2",()=>component.GetModelDoc2(),value=>value==null?"null":"non-null"):component.GetModelDoc2();
   if(model==null)
   {
    int previous=trace?Read("GetSuppression2",()=>component.GetSuppression2(),value=>value.ToString()):component.GetSuppression2();
    if(previous==(int)swComponentSuppressionState_e.swComponentLightweight||previous==(int)swComponentSuppressionState_e.swComponentFullyLightweight)
    {
     resolved.Add(Tuple.Create(component,previous));
     if(trace)Read("SetSuppression2.resolved",()=>component.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved),value=>value.ToString());
     else component.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);
     model=trace?Read("GetModelDoc2.resolved",()=>component.GetModelDoc2(),value=>value==null?"null":"non-null"):component.GetModelDoc2();
    }
   }
   if(model==null){issues.Add("缺失或未能解析："+(trace?Read("Name2.unresolved",()=>component.Name2,value=>value??"<null>"):component.Name2));continue;}
   if((trace?Read("Visible",()=>component.Visible,value=>value.ToString()):component.Visible)==(int)swComponentVisibilityState_e.swComponentHidden)hidden.Add(component);
   var children=trace?Read("GetChildren",()=>component.GetChildren() as object[]??Array.Empty<object>(),value=>"Count="+value.Length):component.GetChildren() as object[]??Array.Empty<object>();
   if(children.Length>0)
   {
    Trace("children.enumerate","begin");
    foreach(var child in children.OfType<IComponent2>().Reverse())pending.Push(child);
    Trace("children.enumerate","end");
   }
   else if(trace?Read("model.IsAssembly",()=>model is IAssemblyDoc,value=>value.ToString()):model is IAssemblyDoc)issues.Add("空子装配体："+(trace?Read("Name2.empty-assembly",()=>component.Name2,value=>value??"<null>"):component.Name2));
   else targets.Add(component);
   if(targets.Count>1000)throw new InvalidOperationException("全部范围超过 1000 个末级零部件，未创建部分爆炸图。");
  }
  Trace("tree","end","Targets="+targets.Count+"; Issues="+issues.Count);
 }
 private static void SetVisible(object item,bool visible)
 {
  if(item is IBody2 body)
  {
   body.HideBody(!visible);
   if(body.Visible!=visible)throw new InvalidOperationException("无法切换实体显示："+body.Name);
  }
  else
  {
   var component=(IComponent2)item;
   component.Visible=(int)(visible?swComponentVisibilityState_e.swComponentVisible:swComponentVisibilityState_e.swComponentHidden);
   if(component.IsHidden(false)==visible)throw new InvalidOperationException("无法切换零部件显示："+component.Name2);
  }
 }
 private double DistanceQuantum()
 {
  if(_partFeatures==null)return 0;
  var units=(_document.GetUnits() as Array)?.Cast<object>().Select(Convert.ToInt32).ToArray();
  if(units==null||units.Length<5)throw new InvalidOperationException("无法读取文档的长度精度，请检查单位设置。");
  return LengthQuantum(units[0],units[1],units[2],units[3],units[4]!=0);
 }
 internal static double LengthQuantum(int unit,int fractionBase,int denominator,int decimals,bool roundFraction)
 {
  double[] metersPerUnit={.001,.01,1,.0254,.3048,.0254,1e-10,1e-9,1e-6,.0000254,.0000000254};
  if(unit<0||unit>=metersPerUnit.Length||decimals<0||decimals>16)throw new InvalidOperationException("当前文档的长度单位或精度不受支持。");
  if((unit==3||unit==5)&&fractionBase==2&&roundFraction)
  {
   if(denominator<=0)throw new InvalidOperationException("文档的分数精度无效。");
   return .0254/denominator;
  }
  return metersPerUnit[unit]*Math.Pow(10,-decimals);
 }
 private string CreateOrReuseAssembly(ExplodeMove[] moves,CancellationToken cancellation,Action<string> check)
 {
  var assembly=(IAssemblyDoc)_document;
  var before=new HashSet<string>(Views(),StringComparer.Ordinal);
  var references=moves.Select(move=>_document.Extension.GetPersistReference3(move.Target.Entity)).ToArray();
  if(references.Any(reference=>!(reference is byte[] bytes)||bytes.Length==0))throw new InvalidOperationException("无法记录移动对象的持久标识，未创建爆炸视图。");
  var byReference=new Dictionary<string,int>(StringComparer.Ordinal);
  for(int index=0;index<references.Length;index++)
  {
   string key=Convert.ToBase64String((byte[])references[index]);
   if(byReference.ContainsKey(key))throw new InvalidOperationException("爆炸移动对象持久标识重复，未创建视图。");
   byReference.Add(key,index);
  }
  // Compare actual native step identities and geometry, not a guessed view title.
  // This remains idempotent across new service instances and model reopen.
  foreach(string candidate in before.Reverse())
  {
   check("正在核对现有爆炸视图…");bool matched=false;
   try
   {
    SetExpanded(candidate,true);
    matched=MatchesAssemblySteps(moves,references,byReference,cancellation);
    if(matched)
    {
     check("已验证现有爆炸步骤…");
     LastAutomaticWasReused=true;
     return candidate;
    }
   }
   finally {if(!matched)SetExpanded(candidate,false);}
  }
  string createdName="";
  try
  {
   check("正在创建原生爆炸视图…");_document.ClearSelection2(true);
   if(!assembly.CreateExplodedView())throw new InvalidOperationException("SOLIDWORKS 未能创建爆炸视图。");
   createdName=Views().SingleOrDefault(name=>!before.Contains(name))??"";
   if(createdName.Length==0)throw new InvalidOperationException("无法确认新建爆炸视图。");
   SetExpanded(createdName,true);
   var configuration=_document.ConfigurationManager.ActiveConfiguration;
   for(int index=configuration.GetNumberOfExplodeSteps()-1;index>=0;index--)
   {
    check("正在整理新视图的初始步骤…");
    var step=configuration.GetExplodeStep(index) as IExplodeStep;
    if(step==null||!configuration.DeleteExplodeStep(step.Name))throw new InvalidOperationException("无法整理新视图的自动爆炸步骤。");
   }
   var selection=_document.ISelectionManager;
   for(int index=0;index<moves.Length;index++)
   {
    check("正在创建爆炸步骤 "+(index+1)+"/"+moves.Length+"…");_document.ClearSelection2(true);
    var target=_document.Extension.GetObjectByPersistReference3(references[index],out int error) as IComponent2;
    var mark=selection.CreateSelectData();mark.Mark=1;
    if(error!=0||target==null||!target.Select4(false,mark,false))throw new InvalidOperationException("移动对象已失效，未生成部分爆炸图。");
    var step=configuration.AddExplodeStep2(moves[index].Distance,moves[index].Axis,moves[index].Reverse,0,0,false,false,false,out error) as IExplodeStep;
    if(step==null||error!=0)throw new InvalidOperationException("第 "+(index+1)+" 个对象爆炸失败（"+error+"）。");
    if(step.ReverseTranslationDirection!=moves[index].Reverse)step.ReverseTranslationDirection=moves[index].Reverse;
    // AddExplodeStep2 requires a rebuild after each call. Commit this native
    // step before progress/cancellation can collapse and delete its view.
    if(!_document.EditRebuild3())throw new InvalidOperationException("第 "+(index+1)+" 个对象爆炸步骤重建失败。");
   }
   check("正在验证原生爆炸结果…");_document.ClearSelection2(true);
   if(!_document.EditRebuild3())throw new InvalidOperationException("爆炸视图重建失败。");
   if(!MatchesAssemblySteps(moves,references,byReference,cancellation))throw new InvalidOperationException("宿主保存的爆炸对象、方向或距离与计划不一致。");
   check("已验证全部爆炸步骤…");
   return createdName;
  }
  catch(Exception error)
  {
   try
   {
    foreach(string added in Views().Where(name=>!before.Contains(name)).ToArray())
    {
     try {SetExpanded(added,false);}catch(Exception collapse){Logger.Error("Incomplete explode collapse failed; attempting owned-view removal",collapse);}
     _document.ClearSelection2(true);
     if(!_document.Extension.SelectByID2(added,"EXPLODEDVIEWS",0,0,0,false,0,null,0)||!_document.Extension.DeleteSelection2(0)||Views().Contains(added))
      throw new InvalidOperationException("新增爆炸视图未能删除："+added);
    }
   }
   catch(Exception cleanup){throw new InvalidOperationException("爆炸未完成，新增视图清理失败："+cleanup.Message,error);}
   if(error is OperationCanceledException)throw;
   throw new InvalidOperationException("爆炸未完成，已移除本次未完成视图："+error.Message,error);
  }
 }
 private bool MatchesAssemblySteps(ExplodeMove[] moves,object[] references,Dictionary<string,int> byReference,CancellationToken cancellation)
 {
  var configuration=_document.ConfigurationManager.ActiveConfiguration;
  if(configuration.GetNumberOfExplodeSteps()!=moves.Length)return false;
  var verified=new HashSet<int>();
  for(int index=0;index<moves.Length;index++)
  {
   cancellation.ThrowIfCancellationRequested();
   var step=configuration.GetExplodeStep(index) as IExplodeStep;
   if(step==null||double.IsNaN(step.RotationAngle)||Math.Abs(step.RotationAngle)>1e-10||step.DivergeFromAxis||step.AutoSpaceComponentsOnDrag)return false;
   var targets=step.GetComponents() as object[]??Array.Empty<object>();
   if(targets.Length!=1)return false;
   object actualReference=_document.Extension.GetPersistReference3(targets[0]);
   int expected=-1;
   if(!(actualReference is byte[] bytes))return false;
   if(!byReference.TryGetValue(Convert.ToBase64String(bytes),out expected))
    expected=Array.FindIndex(references,reference=>_document.Extension.IsSamePersistentID(reference,actualReference)==(int)swObjectEquality.swObjectSame);
   if(expected<0||!verified.Add(expected))return false;
   step.GetExplodeDirection(out int axis);
   if(axis!=moves[expected].Axis||double.IsNaN(step.ExplodeDistance)||Math.Abs(step.ExplodeDistance-moves[expected].Distance)>1e-7||step.ReverseTranslationDirection!=moves[expected].Reverse)return false;
  }
  return true;
 }
}

