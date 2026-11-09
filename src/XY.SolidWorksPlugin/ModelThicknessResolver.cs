using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace XY.SolidWorksPlugin;

internal static class ModelThicknessResolver
{
	private const int MaxGeometryFaces = 2000;
	private static readonly object GeometryCacheSync = new object();
	private static string _geometryCacheKey = "";
	private static double _geometryCacheValue;

	public static bool IsThicknessProperty(string name)
	{
		return string.Equals(name, "厚度", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "板厚", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "料厚", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(name, "SW-Sheet Metal Thickness", StringComparison.OrdinalIgnoreCase);
	}

	public static string Read(IModelDoc2 document)
	{
		double millimeters = ReadMillimeters(document);
		return millimeters > 0
			? millimeters.ToString("0.###", CultureInfo.InvariantCulture)
			: "";
	}

	private static double ReadMillimeters(IModelDoc2 document)
	{
		if (document == null || document.GetType() != (int)swDocumentTypes_e.swDocPART) return 0;

		double propertyThickness = ParseLengthMillimeters(PropertyIo.Read(document, "自定义属性", "厚度"));
		if (propertyThickness > 0) return propertyThickness;
		double sheetMetalThickness = ReadSheetMetalThickness(document);
		if (sheetMetalThickness > 0) return sheetMetalThickness;

		string cacheKey = GeometryCacheKey(document);
		lock (GeometryCacheSync)
		{
			if (string.Equals(_geometryCacheKey, cacheKey, StringComparison.Ordinal)) return _geometryCacheValue;
		}

		double geometryThickness = 0;
		double boundingThickness = 0;
		try
		{
			geometryThickness = SolidWorksProcessFeatureReader.ReadGeometryForThickness(document, MaxGeometryFaces).ThicknessMm;
			if (geometryThickness <= 0) boundingThickness = ReadMinimumBodyDimension(document);
		}
		catch (Exception ex)
		{
			Logger.Trace("Model thickness geometry read skipped: " + ex.Message);
		}
		double resolved = SelectThickness(0, 0, geometryThickness, boundingThickness);
		lock (GeometryCacheSync)
		{
			_geometryCacheKey = cacheKey;
			_geometryCacheValue = resolved;
		}
		return resolved;
	}

	private static double ReadSheetMetalThickness(IModelDoc2 document)
	{
		try
		{
			foreach (string parameterName in new[] { "厚度@钣金", "Thickness@Sheet-Metal", "Thickness@Sheet Metal" })
			{
				if (document.Parameter(parameterName) is IDimension dimension && dimension.SystemValue > 0)
					return dimension.SystemValue * 1000d;
			}
		}
		catch (Exception ex) { Logger.Trace("Sheet metal thickness parameter read skipped: " + ex.Message); }

		try
		{
			IFeature feature = document.FirstFeature() as IFeature;
			for (int count = 0; feature != null && count < 4000; count++)
			{
				string type = (feature.GetTypeName2() ?? "").ToLowerInvariant();
				if (type.Contains("sheetmetal") && feature.GetDefinition() is ISheetMetalFeatureData data && data.Thickness > 0)
					return data.Thickness * 1000d;
				feature = feature.GetNextFeature() as IFeature;
			}
		}
		catch (Exception ex)
		{
			Logger.Trace("Sheet metal thickness read skipped: " + ex.Message);
		}
		return 0;
	}

	private static double ReadMinimumBodyDimension(IModelDoc2 document)
	{
		if (!(document is IPartDoc part)) return 0;
		double minimum = 0;
		foreach (object bodyObject in Objects(part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
		{
			if (!(bodyObject is IBody2 body)) continue;
			double candidate = MinimumBodyDimensionMillimeters(Doubles(body.GetBodyBox()));
			if (candidate > 0 && (minimum <= 0 || candidate < minimum)) minimum = candidate;
		}
		return minimum;
	}

	private static string GeometryCacheKey(IModelDoc2 document)
	{
		string configuration = "";
		try { configuration = document.ConfigurationManager?.ActiveConfiguration?.Name ?? ""; }
		catch (Exception ex) { Logger.Trace("Thickness cache configuration read skipped: " + ex.Message); }
		int updateStamp = 0;
		try { updateStamp = document.GetUpdateStamp(); }
		catch (Exception ex) { Logger.Trace("Thickness cache update stamp read skipped: " + ex.Message); }
		return (document.GetPathName() ?? "") + "|" + (document.GetTitle() ?? "") + "|" + configuration + "|" + updateStamp;
	}

	internal static double ParseLengthMillimeters(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return 0;
		Match match = Regex.Match(value, @"[-+]?\d+(?:[\.,]\d+)?");
		if (!match.Success || !double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number <= 0) return 0;
		string unit = value.ToLowerInvariant();
		if (unit.Contains("inch") || unit.Contains(" in") || unit.Contains("英寸") || unit.Contains("\"")) return number * 25.4d;
		if (unit.Contains("cm") || unit.Contains("厘米")) return number * 10d;
		if ((unit.EndsWith("m") || unit.Contains("米")) && !unit.Contains("mm") && !unit.Contains("毫米")) return number * 1000d;
		return number;
	}

	internal static double SelectThickness(double propertyThickness, double sheetMetalThickness, double geometryThickness, double boundingThickness)
	{
		if (propertyThickness > 0) return propertyThickness;
		if (sheetMetalThickness > 0) return sheetMetalThickness;
		if (geometryThickness > 0) return geometryThickness;
		return boundingThickness > 0 ? boundingThickness : 0;
	}

	internal static double MinimumBodyDimensionMillimeters(double[] bodyBoxMeters)
	{
		if (bodyBoxMeters == null || bodyBoxMeters.Length < 6) return 0;
		double[] dimensions =
		{
			Math.Abs(bodyBoxMeters[3] - bodyBoxMeters[0]) * 1000d,
			Math.Abs(bodyBoxMeters[4] - bodyBoxMeters[1]) * 1000d,
			Math.Abs(bodyBoxMeters[5] - bodyBoxMeters[2]) * 1000d
		};
		return dimensions.Where(dimension => dimension > 0.0001d).DefaultIfEmpty(0).Min();
	}

	private static object[] Objects(object value)
	{
		return value is Array array ? array.Cast<object>().ToArray() : Array.Empty<object>();
	}

	private static double[] Doubles(object value)
	{
		if (!(value is Array array)) return Array.Empty<double>();
		var values = new List<double>(array.Length);
		foreach (object item in array)
		{
			try { values.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture)); }
			catch { values.Add(0); }
		}
		return values.ToArray();
	}
}
