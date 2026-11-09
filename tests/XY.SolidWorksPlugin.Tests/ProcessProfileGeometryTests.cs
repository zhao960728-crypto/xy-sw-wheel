using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessProfileGeometryTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AnonymousRoundedTubeWithThroughWallHolesIsCertified(bool rotated)
	{
		var (planes, cylinders) = Rectangle();
		planes[0].InnerLoopCount = 1;
		planes[1].InnerLoopCount = 1;
		foreach (double x in new[] { -19d, 19 })
			cylinders.Add(Cylinder(3, 2, Math.PI * 2, new[] { x, 0d, 20 }, new[] { 1d, 0, 0 }));
		if (rotated) Rotate(planes, cylinders);
		var result = Analyze(planes, cylinders);
		Assert.Equal("方通", result.ProfileKind);
		Assert.Equal(1, result.StraightTubeBodyCount);
		Assert.Equal(0, result.BendCount);
		Assert.Equal(2, result.ThicknessMm, 3);
		Assert.True(result.GeometryComplete);
	}

	[Fact]
	public void TranslatedTubeCornersWithOppositeAxisSignsRemainCoaxial()
	{
		var (planes, cylinders) = Rectangle();
		foreach (var plane in planes) plane.PointMm[2] += 250;
		for (int index = 0; index < cylinders.Count; index++)
		{
			cylinders[index].AxisPointMm[2] = 250;
			cylinders[index].AxisCenterMm = index % 2 == 0 ? 250 : -250;
			if (index % 2 != 0) cylinders[index].Axis[2] = -1;
		}
		Assert.Equal(1, Analyze(planes, cylinders).StraightTubeBodyCount);
	}

	[Fact]
	public void InvalidNumericalSurfaceCannotCertifyTube()
	{
		var (planes, cylinders) = Round();
		cylinders[0].RadiusMm = double.NaN;
		var result = Analyze(planes, cylinders);
		Assert.Equal(0, result.StraightTubeBodyCount);
		Assert.False(result.GeometryComplete);
	}

	[Fact]
	public void SplitCoplanarWallsPreserveTubeRecognition()
	{
		var (planes, cylinders) = Rectangle();
		var split = planes[0];
		split.AreaMm2 /= 2;
		planes.Add(Plane(split.AreaMm2, split.Normal, split.PointMm));
		Assert.Equal(1, Analyze(planes, cylinders).StraightTubeBodyCount);
	}

	[Fact]
	public void ThickWallTubeIsIndependentOfSheetThicknessLimit()
	{
		var (planes, cylinders) = Rectangle(wall: 15, halfWidth: 80, halfHeight: 60, length: 300, rounded: false);
		var result = Analyze(planes, cylinders);
		Assert.Equal(1, result.StraightTubeBodyCount);
		Assert.Equal(15, result.ThicknessMm, 3);
		Assert.False(result.IsSheetLike);
	}

	[Fact]
	public void NativeFoldedClosedSectionRetainsItsManufacturingBends()
	{
		var (planes, cylinders) = Rectangle();
		var result = ProcessGeometryAnalyzer.AnalyzeBody(Box, planes, cylinders, planes.Count + cylinders.Count, nativeSheetMetal: true);
		Assert.True(result.IsNativeSheetMetal);
		Assert.Equal(0, result.StraightTubeBodyCount);
		Assert.Equal(4, result.BendCount);
		Assert.True(result.IsSheetLike);
	}

	[Theory]
	[InlineData("closed")]
	[InlineData("unequal-wall")]
	[InlineData("partial-hole")]
	[InlineData("unknown-face")]
	public void BoxAndIncompleteEvidenceCannotBeCertifiedAsStockTube(string failure)
	{
		var (planes, cylinders) = Rectangle();
		int extraFaces = 0;
		if (failure == "closed") planes[planes.Count - 1].InnerLoopCount = 0;
		if (failure == "unequal-wall") planes[1].PointMm[0] += 0.8;
		if (failure == "partial-hole") cylinders.Add(Cylinder(3, 2, Math.PI, new[] { 19d, 0, 0 }, new[] { 1d, 0, 0 }));
		if (failure == "unknown-face") extraFaces = 1;
		var result = ProcessGeometryAnalyzer.AnalyzeBody(Box, planes, cylinders, planes.Count + cylinders.Count + extraFaces);
		Assert.Equal(0, result.StraightTubeBodyCount);
		if (extraFaces > 0) Assert.False(result.GeometryComplete);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AnonymousRoundTubeRequiresCoaxialCompleteWallsAndAnnularEnds(bool rotated)
	{
		var (planes, cylinders) = Round();
		if (rotated) Rotate(planes, cylinders);
		var result = Analyze(planes, cylinders);
		Assert.Equal("圆管", result.ProfileKind);
		Assert.Equal(1, result.StraightTubeBodyCount);
		Assert.Equal(2, result.ThicknessMm, 3);
		Assert.Equal(0, result.BendCount);
	}

	[Theory]
	[InlineData("eccentric")]
	[InlineData("capped")]
	[InlineData("short-ring")]
	[InlineData("solid")]
	[InlineData("wrong-end-area")]
	public void RoundTubeBoundaryRejectsOtherCylindricalParts(string failure)
	{
		var (planes, cylinders) = Round();
		if (failure == "eccentric") cylinders[1].AxisPointMm[0] = 1;
		if (failure == "capped") planes[1].InnerLoopCount = 0;
		if (failure == "short-ring")
		{
			planes[0].PointMm[2] = -5;
			planes[1].PointMm[2] = 5;
			foreach (var cylinder in cylinders)
			{
				cylinder.AxisLengthMm = 10;
				cylinder.AreaMm2 = cylinder.RadiusMm * 10 * Math.PI * 2;
			}
		}
		if (failure == "solid") cylinders.RemoveAt(1);
		if (failure == "wrong-end-area") planes[1].AreaMm2 *= 2;
		Assert.Equal(0, Analyze(planes, cylinders).StraightTubeBodyCount);
	}

	[Fact]
	public void MixedBodiesRetainIdentityAndUnscannedBodies()
	{
		var (planes, cylinders) = Rectangle();
		var tube = Analyze(planes, cylinders);
		tube.BodyName = "管件";
		var sheet = new ProcessGeometryFact { BodyName = "折弯板", SolidBodyCount = 1, SheetBodyCount = 1, BendCount = 2, ThicknessMm = 3 };
		var unscanned = new ProcessGeometryFact { BodyName = "未读取实体", SolidBodyCount = 1, GeometryComplete = false };
		var result = ProcessGeometryAnalyzer.Merge(new[] { tube, sheet, unscanned });
		Assert.Equal(3, result.SolidBodyCount);
		Assert.Equal(3, result.BodyFacts.Count);
		Assert.Equal("折弯板", result.BodyFacts[1].BodyName);
		Assert.Equal(2, result.BendCount);
		Assert.Equal(1, result.StraightTubeBodyCount);
		Assert.False(result.GeometryComplete);
	}

	private static readonly double[] Box = { -100d, -100, -200, 100, 100, 200 };
	private static ProcessGeometryFact Analyze(List<ProcessPlaneGeometryFact> planes, List<ProcessCylinderGeometryFact> cylinders)
		=> ProcessGeometryAnalyzer.AnalyzeBody(Box, planes, cylinders, planes.Count + cylinders.Count);

	private static (List<ProcessPlaneGeometryFact>, List<ProcessCylinderGeometryFact>) Rectangle(double wall = 2, double halfWidth = 20,
		double halfHeight = 10, double length = 200, bool rounded = true)
	{
		var planes = new List<ProcessPlaneGeometryFact>();
		foreach (double x in new[] { -halfWidth, -halfWidth + wall, halfWidth - wall, halfWidth })
			planes.Add(Plane(12 * length, new[] { 1d, 0, 0 }, new[] { x, 0d, 0 }));
		foreach (double y in new[] { -halfHeight, -halfHeight + wall, halfHeight - wall, halfHeight })
			planes.Add(Plane(32 * length, new[] { 0d, 1, 0 }, new[] { 0d, y, 0 }));
		foreach (double z in new[] { -length / 2, length / 2 })
			planes.Add(Plane(214, new[] { 0d, 0, 1 }, new[] { 0d, 0, z }, 1));
		var cylinders = new List<ProcessCylinderGeometryFact>();
		if (rounded)
			foreach (double x in new[] { -halfWidth + wall * 2, halfWidth - wall * 2 })
				foreach (double y in new[] { -halfHeight + wall * 2, halfHeight - wall * 2 })
					foreach (double radius in new[] { wall, wall * 2 })
						cylinders.Add(Cylinder(radius, length, Math.PI / 2, new[] { x, y, 0 }, new[] { 0d, 0, 1 }));
		return (planes, cylinders);
	}

	private static (List<ProcessPlaneGeometryFact>, List<ProcessCylinderGeometryFact>) Round()
	{
		var planes = new[] { -100d, 100 }.Select(z => Plane(Math.PI * (100 - 64), new[] { 0d, 0, 1 }, new[] { 0d, 0, z }, 1)).ToList();
		var cylinders = new[] { 10d, 8 }.Select(radius => Cylinder(radius, 200, Math.PI * 2, new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 })).ToList();
		return (planes, cylinders);
	}

	private static ProcessPlaneGeometryFact Plane(double area, double[] normal, double[] point, int loops = 0)
		=> new() { AreaMm2 = area, Normal = normal.ToArray(), PointMm = point.ToArray(), InnerLoopCount = loops };
	private static ProcessCylinderGeometryFact Cylinder(double radius, double length, double angle, double[] point, double[] axis)
		=> new() { RadiusMm = radius, AxisLengthMm = length, AreaMm2 = radius * length * angle, Axis = axis, AxisPointMm = point };

	private static void Rotate(List<ProcessPlaneGeometryFact> planes, List<ProcessCylinderGeometryFact> cylinders)
	{
		foreach (var plane in planes) { plane.Normal = RotateVector(plane.Normal); plane.PointMm = RotateVector(plane.PointMm); }
		foreach (var cylinder in cylinders) { cylinder.Axis = RotateVector(cylinder.Axis); cylinder.AxisPointMm = RotateVector(cylinder.AxisPointMm); }
	}
	private static double[] RotateVector(double[] vector)
	{
		double x = vector[0] * Math.Cos(0.6) - vector[2] * Math.Sin(0.6);
		double z = vector[0] * Math.Sin(0.6) + vector[2] * Math.Cos(0.6);
		return new[] { x, vector[1] * Math.Cos(0.4) - z * Math.Sin(0.4), vector[1] * Math.Sin(0.4) + z * Math.Cos(0.4) };
	}
}
