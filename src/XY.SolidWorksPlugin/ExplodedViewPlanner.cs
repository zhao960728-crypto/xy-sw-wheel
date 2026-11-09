using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace XY.SolidWorksPlugin;
internal sealed class ExplodeTarget
{
 public object Entity { get; set; }
 public string Name { get; set; }
 public double[] Bounds { get; set; }
 public double Center(int axis) => Bounds[axis]+(Bounds[axis+3]-Bounds[axis])/2;
}
internal sealed class ExplodeMove
{
 public ExplodeTarget Target { get; set; }
 public int Axis { get; set; }
 public double Distance { get; set; }
 public bool Reverse { get; set; }
}
internal static class ExplodedViewPlanner
{
 public static List<ExplodeMove> Plan(IReadOnlyList<ExplodeTarget> targets, double gap, int axis, bool reverse, bool around, double distanceQuantum = 0, CancellationToken cancellation = default)
  => PlanWithAnchor(targets, gap, axis, reverse, around, null, distanceQuantum, cancellation);
 internal static List<ExplodeMove> PlanAutomatic(IReadOnlyList<ExplodeTarget> targets, double distanceQuantum, CancellationToken cancellation, out double gap)
 {
  ValidateTargets(targets, cancellation);
  double span=Enumerable.Range(0,3).Max(axis=>targets.Max(target=>target.Bounds[axis+3])-targets.Min(target=>target.Bounds[axis]));
  if(double.IsInfinity(span)) throw new InvalidOperationException("模型边界过大，无法安全计算爆炸移动。");
  gap=Math.Max(.001,Math.Min(1,span*.08));
  return Plan(targets,gap,2,false,true,distanceQuantum,cancellation);
 }
 internal static void ValidateTargets(IReadOnlyList<ExplodeTarget> targets, CancellationToken cancellation = default)
 {
  cancellation.ThrowIfCancellationRequested();
  if(targets==null || targets.Count<2) throw new InvalidOperationException("至少需要两个可见实体或零部件。");
  if(targets.Count>1000) throw new InvalidOperationException("一次最多处理 1000 个对象，请分组选择后创建。");
  foreach(var t in targets) if(t==null||t.Bounds==null||t.Bounds.Length!=6||t.Bounds.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||Enumerable.Range(0,3).Any(a=>t.Bounds[a]>t.Bounds[a+3]||double.IsInfinity(t.Bounds[a+3]-t.Bounds[a]))) throw new InvalidOperationException("无法读取对象边界，请先解析轻量化对象。");
  if(targets.Distinct().Count()!=targets.Count) throw new InvalidOperationException("移动列表含有重复对象，请重新预览。");
  var entities=targets.Where(target=>target.Entity!=null).Select(target=>target.Entity).ToArray();
  if(entities.Distinct().Count()!=entities.Length) throw new InvalidOperationException("移动列表含有重复实体，请重新预览。");
 }
 public static List<ExplodeMove> PlanWithAnchor(IReadOnlyList<ExplodeTarget> targets, double gap, int axis, bool reverse, bool around, ExplodeTarget fixedTarget, double distanceQuantum = 0, CancellationToken cancellation = default)
 {
  ValidateTargets(targets,cancellation);
  if(double.IsNaN(gap)||double.IsInfinity(gap)||gap<=0||gap>10||axis<0||axis>2) throw new InvalidOperationException("请设置有效的方向和间距（0～10000 mm）。");
  if(fixedTarget!=null && (!around || !targets.Contains(fixedTarget))) throw new InvalidOperationException("保留对象必须位于本次四周分散范围内，请重新预览并选择。");
  if(double.IsNaN(distanceQuantum)||double.IsInfinity(distanceQuantum)||distanceQuantum<0) throw new InvalidOperationException("无法读取爆炸距离精度。");
  var moves=new List<ExplodeMove>();
  if(!around)
  {
   var ordered=targets.OrderBy(t=>(reverse?-1:1)*t.Center(axis)).ToArray();
   moves.Add(new ExplodeMove {Target=ordered[0],Axis=axis});
   double end=reverse?-ordered[0].Bounds[axis]:ordered[0].Bounds[axis+3];
   for(int i=1;i<ordered.Length;i++) { cancellation.ThrowIfCancellationRequested();var t=ordered[i];double near=reverse?-t.Bounds[axis+3]:t.Bounds[axis];double far=reverse?-t.Bounds[axis]:t.Bounds[axis+3];double distance=RoundOutward(Math.Max(gap*i,end+gap-near),distanceQuantum);moves.Add(new ExplodeMove {Target=t,Axis=axis,Reverse=reverse,Distance=distance});end=far+distance; }
   return Checked(moves);
  }
  double[] min=Enumerable.Range(0,3).Select(a=>targets.Min(t=>t.Bounds[a])).ToArray(),max=Enumerable.Range(0,3).Select(a=>targets.Max(t=>t.Bounds[a+3])).ToArray();
  double[] center=Enumerable.Range(0,3).Select(a=>min[a]+(max[a]-min[a])/2).ToArray();
  if(center.Any(v=>double.IsInfinity(v)||double.IsNaN(v))) throw new InvalidOperationException("模型边界过大，无法安全计算爆炸移动。");
  var anchor=fixedTarget ?? targets.OrderBy(t=>Enumerable.Range(0,3).Sum(a=>Math.Pow((t.Center(a)-center[a])/Math.Max(max[a]-min[a],1e-9),2))).First();
  moves.Add(new ExplodeMove {Target=anchor,Axis=axis});
  double[] origin=Enumerable.Range(0,3).Select(anchor.Center).ToArray();
  var grouped=targets.Where(t=>t!=anchor).Select(t=>{int direction=Enumerable.Range(0,3).OrderByDescending(a=>Math.Abs(t.Center(a)-origin[a])).First();if(Math.Abs(t.Center(direction)-origin[direction])<1e-9)direction=axis;return new ExplodeMove {Target=t,Axis=direction,Reverse=t.Center(direction)<origin[direction]};}).GroupBy(m=>m.Axis*2+(m.Reverse?1:0));
  foreach(var group in grouped)
  {
   cancellation.ThrowIfCancellationRequested();
   var first=group.First();int a=first.Axis;bool rev=first.Reverse;double end=rev?-min[a]:max[a];
   foreach(var move in group.OrderBy(m=>(rev?-1:1)*m.Target.Center(a))) {double near=rev?-move.Target.Bounds[a+3]:move.Target.Bounds[a];double far=rev?-move.Target.Bounds[a]:move.Target.Bounds[a+3];move.Distance=RoundOutward(Math.Max(gap,end+gap-near),distanceQuantum);end=far+move.Distance;moves.Add(move);}
  }
  SeparateIsometric(moves, gap, distanceQuantum, cancellation);
  return Checked(moves);
 }
 // Keep every object readable in the standard isometric view. Merely separating
 // world boxes still leaves front/back panels visually covering one another.
 private static void SeparateIsometric(List<ExplodeMove> moves, double gap, double distanceQuantum, CancellationToken cancellation)
 {
  double[][] projection={new[]{1/Math.Sqrt(2),0d,-1/Math.Sqrt(2)},new[]{-1/Math.Sqrt(6),2/Math.Sqrt(6),-1/Math.Sqrt(6)}};
  var placed=new List<double[]>();var projected=new List<double[]>();
  foreach(var move in moves)
  {
   cancellation.ThrowIfCancellationRequested();
   double[] source=move.Target.Bounds;double[] screen=ProjectIsometric(source);
   var worldDirection=Enumerable.Range(0,3).Select(a=>a==move.Axis?(move.Reverse?-1d:1d):0d).ToArray();
   var screenDirection=projection.Select(row=>row[move.Axis]*(move.Reverse?-1:1)).ToArray();
   var forbidden=new List<Tuple<double,double>>();
   for(int i=0;i<placed.Count;i++)
   {
    if((i&31)==0)cancellation.ThrowIfCancellationRequested();
    AddForbidden(forbidden,source,placed[i],worldDirection,gap);
    AddForbidden(forbidden,screen,projected[i],screenDirection,gap);
   }
   if(placed.Count>0) foreach(var interval in forbidden.OrderBy(pair=>pair.Item1))
    if(move.Distance>interval.Item1-1e-10&&move.Distance<interval.Item2) move.Distance=RoundOutward(interval.Item2+1e-9,distanceQuantum);
   var bounds=(double[])source.Clone();bounds[move.Axis]+=(move.Reverse?-1:1)*move.Distance;bounds[move.Axis+3]+=(move.Reverse?-1:1)*move.Distance;
   placed.Add(bounds);projected.Add(ProjectIsometric(bounds));
  }
 }
 internal static double[] ProjectIsometric(double[] bounds)
 {
  double[] result={double.PositiveInfinity,double.PositiveInfinity,double.NegativeInfinity,double.NegativeInfinity};
  for(int corner=0;corner<8;corner++)
  {
   double x=bounds[(corner&1)==0?0:3],y=bounds[(corner&2)==0?1:4],z=bounds[(corner&4)==0?2:5];
   double u=(x-z)/Math.Sqrt(2),v=(-x+2*y-z)/Math.Sqrt(6);
   result[0]=Math.Min(result[0],u);result[1]=Math.Min(result[1],v);result[2]=Math.Max(result[2],u);result[3]=Math.Max(result[3],v);
  }
  return result;
 }
 private static void AddForbidden(List<Tuple<double,double>> intervals,double[] source,double[] other,double[] direction,double gap)
 {
  int dimensions=direction.Length;double low=double.NegativeInfinity,high=double.PositiveInfinity;
  for(int axis=0;axis<dimensions;axis++)
  {
   double a=other[axis]-source[axis+dimensions]-gap,b=other[axis+dimensions]-source[axis]+gap,c=direction[axis];
   if(Math.Abs(c)<1e-12) {if(a>=0||b<=0)return;continue;}
   low=Math.Max(low,Math.Min(a/c,b/c));high=Math.Min(high,Math.Max(a/c,b/c));
  }
  if(low<high) intervals.Add(Tuple.Create(low,high));
 }
 private static double RoundOutward(double distance,double quantum)
  => quantum>0 ? Math.Ceiling(distance/quantum)*quantum : distance;
 private static List<ExplodeMove> Checked(List<ExplodeMove> moves)
 {
  if(moves.Any(m=>double.IsNaN(m.Distance)||double.IsInfinity(m.Distance)||m.Distance<0||
   double.IsInfinity(m.Target.Bounds[m.Axis]+(m.Reverse?-1:1)*m.Distance)||double.IsInfinity(m.Target.Bounds[m.Axis+3]+(m.Reverse?-1:1)*m.Distance)))
   throw new InvalidOperationException("移动距离超出可计算范围，请检查模型尺寸。");
  return moves;
 }
}
