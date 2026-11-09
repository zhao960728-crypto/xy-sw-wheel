using System;
using System.Drawing;

namespace XY.SolidWorksPlugin;

internal sealed class RadialTextFitResult
{
	public string DisplayText { get; }

	public string[] Lines { get; }

	public float FontSize { get; }

	public SizeF TextSize { get; }

	public bool FullText { get; }

	public PointF Center { get; }

	public RectangleF BlockBounds { get; }

	public RectangleF[] LineBounds { get; }

	public RadialTextFitResult(string displayText, string[] lines, float fontSize, SizeF textSize, bool fullText, PointF center = default, RectangleF[] lineBounds = null)
	{
		DisplayText = displayText;
		Lines = lines;
		FontSize = fontSize;
		TextSize = textSize;
		FullText = fullText;
		Center = center;
		if (lineBounds != null && lineBounds.Length == lines.Length)
		{
			LineBounds = lineBounds;
			BlockBounds = Union(lineBounds);
		}
		else
		{
			BlockBounds = new RectangleF(center.X - textSize.Width * 0.5f, center.Y - textSize.Height * 0.5f, textSize.Width, textSize.Height);
			float lineHeight = lines.Length == 0 ? textSize.Height : textSize.Height / lines.Length;
			LineBounds = new RectangleF[lines.Length];
			for (int index = 0; index < lines.Length; index++)
			{
				LineBounds[index] = new RectangleF(BlockBounds.Left, BlockBounds.Top + index * lineHeight, BlockBounds.Width, lineHeight);
			}
		}
	}

	private static RectangleF Union(RectangleF[] bounds)
	{
		if (bounds.Length == 0)
		{
			return RectangleF.Empty;
		}
		RectangleF union = bounds[0];
		for (int index = 1; index < bounds.Length; index++)
		{
			union = RectangleF.Union(union, bounds[index]);
		}
		return union;
	}
}

internal static class RadialTextLayout
{
	public static SizeF AvailableArea(float radius, float ringWidth, int slots)
	{
		float safeRingWidth = Math.Max(1f, ringWidth);
		int safeSlots = Math.Max(1, slots);
		float width;
		if (safeSlots <= 2)
		{
			width = Math.Max(8f, radius) * 1.6f;
		}
		else
		{
			double halfSweep = Math.PI / safeSlots;
			width = 2f * Math.Max(8f, radius) * (float)Math.Sin(halfSweep) * 0.98f;
		}
		return new SizeF(Math.Max(8f, width), Math.Max(8f, safeRingWidth * 0.88f));
	}

	public static float MaximumFontSize(int itemFontSizePx, float previewScale, float availableHeight)
	{
		float scale = Math.Max(0.05f, previewScale);
		if (itemFontSizePx > 0)
		{
			return Math.Max(1f, itemFontSizePx * scale);
		}
		return Math.Max(1f, Math.Min(30f * scale, Math.Max(1f, availableHeight * 0.74f)));
	}

	public static float MinimumReadableFontSize(float previewScale, float maximumFontSize)
	{
		float scale = Math.Max(0.05f, previewScale);
		return Math.Min(Math.Max(1f, maximumFontSize), Math.Max(1f, 15f * scale));
	}

	public static int MaximumAutoLines(int labelLength)
	{
		return labelLength <= 4 ? 2 : 4;
	}

	// The absolute lower bound we ever render auto-fit text at. The original
	// 2.5 px floor was readable only on paper — at real screen scale 2.5 px
	// is a smudge of dots. 4 * previewScale gives a clearer floor for any
	// preview size (4 px at 1:1, 2 px at 0.5x — already very small but at
	// least the shape of a character), while staying below the readable
	// target so the binary search still has room to search.
	public static float MinimumFallbackFontSize(float previewScale) =>
		Math.Max(2.5f, 4f * Math.Max(0.05f, previewScale));

	// When a user-specified font size literally does not fit (e.g. 24 px on a
	// 16-slot outer ring band) the renderer used to let the clipping path chop
	// the text. We now ask for a smaller font instead: the user's saved size
	// is treated as the upper bound, and we scale down by the most restrictive
	// axis (width or height) with 5 % slack so antialiasing stays inside the
	// cell path. Returns the new size, or the original size if it already fit.
	public static float ShrinkFixedFontSizeToFit(float maxSize, SizeF textSize, float availableWidth, float availableHeight)
	{
		if (textSize.Width <= availableWidth && textSize.Height <= availableHeight)
		{
			return maxSize;
		}
		float shrinkScale = Math.Min(
			availableWidth / Math.Max(1f, textSize.Width),
			availableHeight / Math.Max(1f, textSize.Height));
		return Math.Max(1f, maxSize * shrinkScale * 0.95f);
	}
}
