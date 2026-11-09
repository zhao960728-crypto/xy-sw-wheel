using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

// Counts connected internal cavities from actual B-rep face adjacency. Opposite
// openings of one through hole share its wall component; blind holes separated
// by material remain distinct even when their centres and axes coincide.
internal static class ProcessSheetCutoutTopology
{
    internal sealed class FaceFact
    {
        internal string Key = "";
        internal double[] Normal = Array.Empty<double>(), PointMm = Array.Empty<double>(), BoxMm = Array.Empty<double>();
        internal readonly HashSet<string> Neighbours = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<string[]> InnerWalls = new List<string[]>();
        internal string[] OuterWalls = Array.Empty<string>();
        internal bool IsPlane => Normal.Length == 3 && PointMm.Length == 3;
    }
    internal sealed class Result
    {
        internal int Count;
        internal bool Reliable;
        internal string Evidence = "";
    }
    internal static Result Read(IModelDoc2 document, IEnumerable<IFace2> faces, double thicknessMm, bool hasBends, int timeBudgetMs = 5000)
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            void Budget() { if (clock.ElapsedMilliseconds >= Math.Max(1, timeBudgetMs)) throw new InvalidOperationException("内轮廓拓扑达到本次读取时间限额"); }
            var references = new Dictionary<string, FaceFact>(StringComparer.Ordinal);
            string Key(object value)
            {
                byte[] bytes = document.Extension.GetPersistReference3(value) as byte[];
                if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("无法取得面拓扑引用");
                return Convert.ToBase64String(bytes);
            }
            foreach (IFace2 face in faces)
            {
                Budget();
                var fact = new FaceFact { Key = Key(face) };
                if (references.ContainsKey(fact.Key)) throw new InvalidOperationException("面拓扑引用重复");
                ISurface surface = face.GetSurface() as ISurface;
                if (surface == null) throw new InvalidOperationException("无法读取内轮廓邻接面");
                bool planar = surface.IsPlane();
                if (planar)
                {
                    double[] plane = Numbers(surface.PlaneParams);
                    if (plane.Length < 6) throw new InvalidOperationException("平面参数不完整");
                    fact.Normal = Unit(plane.Take(3).ToArray());
                    fact.PointMm = plane.Skip(3).Take(3).Select(x => x * 1000).ToArray();
                    fact.BoxMm = Numbers(face.GetBox()).Select(x => x * 1000).ToArray();
                }
                foreach (IEdge edge in Items(face.GetEdges()).OfType<IEdge>())
                {
                    Budget();
                    var adjacent = Items(edge.GetTwoAdjacentFaces2()).OfType<IFace2>().Select(Key).ToArray();
                    // A cylindrical seam validly references the same face twice.
                    if (adjacent.Length != 2 || !adjacent.Contains(fact.Key)) throw new InvalidOperationException("存在非封闭或不可验证的邻接边");
                    foreach (string key in adjacent.Distinct().Where(key => key != fact.Key)) fact.Neighbours.Add(key);
                }
                if (planar)
                foreach (ILoop2 loop in Items(face.GetLoops()).OfType<ILoop2>())
                {
                    var walls = new HashSet<string>(StringComparer.Ordinal);
                    foreach (IEdge edge in Items(loop.GetEdges()).OfType<IEdge>())
                        foreach (IFace2 adjacent in Items(edge.GetTwoAdjacentFaces2()).OfType<IFace2>())
                        { string key = Key(adjacent); if (key != fact.Key) walls.Add(key); }
                    if (walls.Count == 0) throw new InvalidOperationException("闭合轮廓缺少可验证的相邻壁面");
                    if (loop.IsOuter()) fact.OuterWalls = walls.ToArray(); else fact.InnerWalls.Add(walls.ToArray());
                }
                references.Add(fact.Key, fact);
            }
            Budget();
            return Analyze(references.Values.ToArray(), thicknessMm, hasBends);
        }
        catch (Exception ex) { return new Result { Evidence = "内轮廓拓扑读取不完整：" + ex.Message + "；未写入内轮廓数" }; }
    }

    internal static Result Analyze(IReadOnlyList<FaceFact> faces, double thicknessMm, bool hasBends = false)
    {
        var result = new Result();
        if (faces == null || faces.Count == 0 || thicknessMm <= 0) { result.Evidence = "缺少完整实体或可靠板厚，未确认内轮廓总数"; return result; }
        var byKey = faces.ToDictionary(face => face.Key, StringComparer.Ordinal);
        if (faces.Any(face => face.Neighbours.Any(key => !byKey.ContainsKey(key)) || face.InnerWalls.SelectMany(loop => loop).Any(key => !byKey.ContainsKey(key))))
        { result.Evidence = "内轮廓邻接面超出当前完整实体，未确认总数"; return result; }
        var skins = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<FaceFact>();
        foreach (var face in faces.Where(face => face.IsPlane && face.InnerWalls.Count > 0))
        {
            var opposite = faces.Where(other => other != face && other.IsPlane && Math.Abs(Dot(face.Normal, other.Normal)) > 0.998
                && Math.Abs(Math.Abs(Dot(face.Normal, Sub(other.PointMm, face.PointMm))) - thicknessMm) <= Math.Max(0.02, thicknessMm * 0.01)
                && ProjectedOverlap(face, other)).ToArray();
            if (opposite.Length == 0) continue; // A counterbore shoulder is a cavity wall, not an exterior sheet skin.
            entries.Add(face); skins.Add(face.Key);
            foreach (var other in opposite) skins.Add(other.Key);
        }
        var parent = faces.Where(face => !skins.Contains(face.Key)).ToDictionary(face => face.Key, face => face.Key, StringComparer.Ordinal);
        string Root(string key) { while (parent[key] != key) { parent[key] = parent[parent[key]]; key = parent[key]; } return key; }
        void Union(string first, string second) { string a = Root(first), b = Root(second); if (a != b) parent[b] = a; }
        foreach (var face in faces.Where(face => parent.ContainsKey(face.Key)))
            foreach (string other in face.Neighbours.Where(parent.ContainsKey)) Union(face.Key, other);
        var cavities = new List<string>();
        foreach (var face in entries)
        foreach (var loop in face.InnerWalls)
        {
            string[] walls = loop.Where(parent.ContainsKey).Distinct().ToArray();
            if (walls.Length == 0) { result.Evidence = "内轮廓未形成独立壁面，未确认总数"; return result; }
            for (int i = 1; i < walls.Length; i++) Union(walls[0], walls[i]);
            cavities.Add(walls[0]);
        }
        var roots = new HashSet<string>(cavities.Select(Root), StringComparer.Ordinal);
        result.Count = roots.Count;
        var exterior = new HashSet<string>(faces.Where(face => skins.Contains(face.Key)).SelectMany(face => face.OuterWalls).Where(parent.ContainsKey).Select(Root), StringComparer.Ordinal);
        if (roots.Overlaps(exterior)) { result.Evidence = "内轮廓壁面与外周边界连通，需展开复核，未写入内轮廓数"; return result; }
        var unexplained = faces.Where(face => face.InnerWalls.Count > 0 && !skins.Contains(face.Key))
            .Any(face => !parent.ContainsKey(face.Key) || !roots.Contains(Root(face.Key)));
        if (unexplained) { result.Evidence = "存在未关联到板面开口的内部轮廓，需核对台阶/局部厚度，未写入内轮廓数"; return result; }
        if (hasBends)
        {
            result.Evidence = "已按相邻壁面识别 " + result.Count + " 个独立闭合平面开口；折弯曲面上的跨面开口尚需展开核对，未写入内轮廓总数";
            return result;
        }
        result.Reliable = true;
        result.Evidence = "完整实体拓扑：" + result.Count + " 个独立内部壁面连通区；同一穿孔的两侧开口合并，互不连通的盲孔分别计数";
        return result;
    }
    private static bool ProjectedOverlap(FaceFact a, FaceFact b)
    {
        if (a.BoxMm.Length < 6 || b.BoxMm.Length < 6) return true; // Pure topology tests omit optional spatial boxes.
        int dominant = Enumerable.Range(0, 3).OrderByDescending(i => Math.Abs(a.Normal[i])).First();
        return Enumerable.Range(0, 3).Where(i => i != dominant).All(i => Math.Min(a.BoxMm[i + 3], b.BoxMm[i + 3]) - Math.Max(a.BoxMm[i], b.BoxMm[i]) > 0.001);
    }
    private static IEnumerable<object> Items(object value) => value is Array array ? array.Cast<object>() : Array.Empty<object>();
    private static double[] Numbers(object value) => Items(value).Select(Convert.ToDouble).ToArray();
    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double[] Unit(double[] v) { double length = Math.Sqrt(Dot(v, v)); if (length < 1e-12) throw new InvalidOperationException("平面法线无效"); return v.Select(x => x / length).ToArray(); }
}
