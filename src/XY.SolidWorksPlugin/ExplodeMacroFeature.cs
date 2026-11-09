using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

namespace XY.SolidWorksPlugin;

[ComVisible(true), Guid("9A03103D-61D1-4078-87B7-D342D6850F7B"), ProgId(ProgramId), ClassInterface(ClassInterfaceType.None)]
public sealed class ExplodeMacroFeature : ISwComFeature
{
 internal const string ProgramId = "XY.SolidWorksPlugin.Explode";
 internal const string BaseName = "XYExplodeV1";
 internal const string ParameterName = "MovesV1";
 internal const string ConfigurationParameterName = "ConfigurationV1";
 internal sealed class Move { public string Name { get; set; } public double[] Delta { get; set; } }
 internal static Move[] Parse(string json)
 {
  Move[] moves;
  try
  {
   if(string.IsNullOrWhiteSpace(json)||json.Length>1048576)throw new InvalidOperationException("爆炸特征参数无效或过大。");
   moves = new JavaScriptSerializer { MaxJsonLength=1048576 }.Deserialize<Move[]>(json);
  }
  catch(Exception error) when(!(error is InvalidOperationException)) { throw new InvalidOperationException("爆炸特征参数无法读取。",error); }
  if(moves == null || moves.Length == 0 || moves.Length > 1000 || moves.Any(move => move == null || string.IsNullOrWhiteSpace(move.Name) || move.Delta == null || move.Delta.Length != 3 || move.Delta.Any(value => double.IsNaN(value) || double.IsInfinity(value)))) throw new InvalidOperationException("爆炸特征参数无效。");
  if(moves.Select(move=>move.Name).Distinct(StringComparer.Ordinal).Count()!=moves.Length) throw new InvalidOperationException("爆炸实体名称重复。");
  return moves;
 }
 public object Regenerate(object app, object modelDoc, object feature)
 {
  try
  {
   var data = (IMacroFeatureData)((IFeature)feature).GetDefinition();
   data.EnableMultiBodyConsume=true;
   data.GetStringByName(ParameterName, out string json);
   var moves=Parse(json);
   var bodies=(data.EditBodies as object[] ?? Array.Empty<object>()).OfType<IBody2>().ToArray();
   bool trace=string.Equals(System.Environment.GetEnvironmentVariable("XY_EXPLODE_TRACE"),"1",StringComparison.Ordinal);
   if(trace)Logger.Info("Explode macro input. Feature="+((IFeature)feature).Name+"; Moves="+(json.Length>2048?json.Substring(0,2048)+"...":json)+"; Bodies="+DescribeBodies(bodies));
   if(bodies.Length!=moves.Length) return Reject("爆炸输入实体数量不一致（"+bodies.Length+"/"+moves.Length+"，宿主="+data.GetEditBodiesCount()+"），请压缩此特征并重新创建。");
   var byName=moves.ToDictionary(move=>move.Name,StringComparer.Ordinal);
   var bodyIds=moves.Select((move,index)=>new {move.Name,Id=index+1}).ToDictionary(item=>item.Name,item=>item.Id,StringComparer.Ordinal);
   var names=bodies.Select(body=>body.Name).ToArray();
   if(names.Distinct(StringComparer.Ordinal).Count()!=bodies.Length||names.Any(name=>name==null||!byName.ContainsKey(name)))return Reject("爆炸输入实体名称已变化或重复，请压缩此特征并重新创建。 Expected="+string.Join("|",moves.Take(8).Select(move=>move.Name))+"; Actual="+string.Join("|",names.Take(8).Select(name=>name??"<null>")));
   var math=(IMathUtility)((ISldWorks)app).GetMathUtility();
   var transforms=bodies.Select((body,index)=>{var d=byName[names[index]].Delta;return math.CreateTransform(new[]{1d,0,0,0,1,0,0,0,1,d[0],d[1],d[2],1,0,0,0}) as MathTransform;}).ToArray();
   if(transforms.Any(transform=>transform==null))return Reject("无法建立爆炸实体的移动变换。");
   for(int index=0;index<bodies.Length;index++)
   {
    var move=byName[names[index]];
    // EditBodies are the temporary input bodies supplied for this rebuild.
    // Modify them in place; returning replacement copies loses native identity
    // when a later feature consumes the same upstream bodies.
    var copy=bodies[index];
    if(!copy.ApplyTransform(transforms[index])) return Reject("无法移动爆炸实体："+move.Name);
    data.GetEntitiesNeedUserId(copy,out object newFaces,out object newEdges);
    var faces=newFaces as object[] ?? Array.Empty<object>();
    var edges=newEdges as object[] ?? Array.Empty<object>();
    int bodyId=bodyIds[move.Name];
    for(int i=0;i<faces.Length;i++) if(!data.SetFaceUserId((Face2)faces[i],bodyId,i+1)) return Reject("无法保存爆炸实体面标识："+move.Name);
    for(int i=0;i<edges.Length;i++) if(!data.SetEdgeUserId((Edge)edges[i],bodyId,faces.Length+i+1)) return Reject("无法保存爆炸实体边标识："+move.Name);
   }
   if(trace)Logger.Info("Explode macro transformed inputs; returning modify-success=True. Bodies="+DescribeBodies(bodies));
   return true;
  }
  catch(Exception error) { Logger.Error("Explode feature rebuild failed",error); return "爆炸特征重建失败："+error.Message; }
 }
 private static string Reject(string message) {Logger.Error("Explode macro feature rejected rebuild: "+message);return message;}
 private static string DescribeBodies(IBody2[] bodies)=>string.Join(" | ",bodies.Take(8).Select(body=>
 {
  try {return body.Name+"=["+PartExplodeFeatures.FormatBounds(body.GetBodyBox() as double[])+"]";}
  catch(Exception error){return "<unreadable:"+error.Message+">";}
 }));
 public object Security(object app, object modelDoc, object feature) => 0;
 public object Edit(object app, object modelDoc, object feature)
 {
  ToolNotice.Show("压缩此特征可恢复原位，解除压缩可再次展开；再次运行一键爆炸会复用或更新本特征，不会增加重复特征。", "一键爆炸特征");
  return true;
 }
}
