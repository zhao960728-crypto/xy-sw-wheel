using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed partial class RadialMenuControl : Control
{
	private static readonly float[] TextPlacementFractions = { 0.56f, 0.51f, 0.61f, 0.46f, 0.66f, 0.36f, 0.76f, 0.26f, 0.86f };

	// --- Surface colours ---
	// Every colour the wheel paints is read from the scene appearance, so what
	// the settings dialog edits is exactly what the wheel shows.
	private WheelAppearance Wheel => _scene.Appearance ?? WheelAppearance.DarkGlass();

	private Color WheelLineColor => Wheel.GetBorderColor();
	private Color WheelTextColor => Wheel.GetTextColor();
	private Color WheelAccentColor => Wheel.GetSelectedColor();
	private Color WheelHoverColor => Wheel.GetHoverColor();

	// 100 is fully opaque. The stored value is clamped because a fully
	// transparent disc would leave the wheel unreadable over a model.
	private int SurfaceAlpha => Math.Max(5, Math.Min(100, _scene.SurfaceOpacityPercent > 0 ? _scene.SurfaceOpacityPercent : 65));

	private Brush CreateSurfaceBrush(RectangleF bounds)
	{
		Color surface = Wheel.GetSurfaceColor();
		// A colour-keyed popup cannot preserve per-pixel alpha: blending here
		// would permanently tint the surface with the (near-black) key colour.
		// Its window applies the surface opacity over the actual model instead.
		int alpha = SurfaceOpacityAppliedByWindow ? 100 : SurfaceAlpha;
		if (!string.Equals(Wheel.FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) || bounds.Width <= 0 || bounds.Height <= 0)
			return new SolidBrush(Color.FromArgb(alpha * 255 / 100, surface));
		return new LinearGradientBrush(bounds,
			Color.FromArgb(alpha * 255 / 100, surface),
			Color.FromArgb(alpha * 255 / 100, WheelAppearance.ParseColor(Wheel.EndColor, surface)),
			Wheel.GradientAngle);
	}

	// --- CAD shared text layout cache (cross-instance) ---
	private static readonly ConcurrentDictionary<string, FittedTextLayout> SharedTextLayoutCache = new(StringComparer.Ordinal);

	// --- CAD diagnostics (per-instance) ---
	private readonly List<TextLayoutDiagnostic> _textDiagnostics = new();
	private int _textLayoutCacheHits;
	private int _textLayoutCacheMisses;

	internal sealed class TextLayoutDiagnostic
	{
		public string Label { get; set; } = "";
		public float LogicalFontSizePx { get; set; }
		public bool FullText { get; set; }
		public bool FitsBounds { get; set; }
		public int LineCount { get; set; }
	}

	private sealed class FittedTextLayout
	{
		public string[] Lines { get; set; } = Array.Empty<string>();
		public float FontSizePx { get; set; }
		public float LogicalFontSizePx { get; set; }
		public PointF Center { get; set; }
		public bool FullText { get; set; }
		public bool FitsBounds { get; set; }
	}

	// --- SW AutoTextLayoutKey ---
	private readonly struct AutoTextLayoutKey : IEquatable<AutoTextLayoutKey>
	{
		private readonly string _label;
		private readonly string _fontName;
		private readonly float _centerX;
		private readonly float _centerY;
		private readonly float _radius;
		private readonly float _ringWidth;
		private readonly float _availableWidth;
		private readonly float _availableHeight;
		private readonly float _angle;
		private readonly float _previewScale;
		private readonly float _dpiX;
		private readonly float _dpiY;

		public AutoTextLayoutKey(string label, string fontName, PointF center, float radius, float ringWidth, float availableWidth, float availableHeight, float angle, float previewScale, Graphics graphics)
		{
			_label = label ?? "";
			_fontName = fontName ?? "";
			_centerX = center.X;
			_centerY = center.Y;
			_radius = radius;
			_ringWidth = ringWidth;
			_availableWidth = availableWidth;
			_availableHeight = availableHeight;
			_angle = angle;
			_previewScale = previewScale;
			_dpiX = graphics.DpiX;
			_dpiY = graphics.DpiY;
		}

		public bool Equals(AutoTextLayoutKey other)
		{
			return string.Equals(_label, other._label, StringComparison.Ordinal)
				&& string.Equals(_fontName, other._fontName, StringComparison.OrdinalIgnoreCase)
				&& _centerX.Equals(other._centerX)
				&& _centerY.Equals(other._centerY)
				&& _radius.Equals(other._radius)
				&& _ringWidth.Equals(other._ringWidth)
				&& _availableWidth.Equals(other._availableWidth)
				&& _availableHeight.Equals(other._availableHeight)
				&& _angle.Equals(other._angle)
				&& _previewScale.Equals(other._previewScale)
				&& _dpiX.Equals(other._dpiX)
				&& _dpiY.Equals(other._dpiY);
		}

		public override bool Equals(object obj)
		{
			return obj is AutoTextLayoutKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = StringComparer.Ordinal.GetHashCode(_label);
				hash = hash * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_fontName);
				hash = hash * 397 ^ _centerX.GetHashCode();
				hash = hash * 397 ^ _centerY.GetHashCode();
				hash = hash * 397 ^ _radius.GetHashCode();
				hash = hash * 397 ^ _ringWidth.GetHashCode();
				hash = hash * 397 ^ _availableWidth.GetHashCode();
				hash = hash * 397 ^ _availableHeight.GetHashCode();
				hash = hash * 397 ^ _angle.GetHashCode();
				hash = hash * 397 ^ _previewScale.GetHashCode();
				hash = hash * 397 ^ _dpiX.GetHashCode();
				return hash * 397 ^ _dpiY.GetHashCode();
			}
		}
	}

	// --- SW FontKey ---
	private readonly struct FontKey : IEquatable<FontKey>
	{
		private readonly string _name;
		private readonly float _size;

		public FontKey(string name, float size)
		{
			_name = name ?? "";
			_size = size;
		}

		public bool Equals(FontKey other)
		{
			return string.Equals(_name, other._name, StringComparison.OrdinalIgnoreCase) && _size.Equals(other._size);
		}

		public override bool Equals(object obj)
		{
			return obj is FontKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return StringComparer.OrdinalIgnoreCase.GetHashCode(_name) * 397 ^ _size.GetHashCode();
			}
		}
	}

	// --- HitResult ---
	private readonly struct HitResult
	{
		public static readonly HitResult None = new HitResult(-1, -1, center: false);
		public static readonly HitResult Center = new HitResult(-1, -1, center: true);

		public int RingIndex { get; }
		public int Slot { get; }
		public bool IsCenter { get; }

		public bool HasSlot
		{
			get
			{
				if (RingIndex >= 0)
					return Slot >= 0;
				return false;
			}
		}

		public HitResult(int ringIndex, int slot, bool center)
		{
			RingIndex = ringIndex;
			Slot = slot;
			IsCenter = center;
		}
	}

	// --- RingLayout ---
	private sealed class RingLayout
	{
		public RingConfig Ring { get; }
		public float InnerRadius { get; }
		public float OuterRadius { get; }
		public float Width => Math.Max(1f, OuterRadius - InnerRadius);

		public RingLayout(RingConfig ring, float innerRadius, float outerRadius)
		{
			Ring = ring;
			InnerRadius = innerRadius;
			OuterRadius = outerRadius;
		}
	}

	// --- Fields ---
	private PluginConfig _config = new PluginConfig();
	private SceneMenuConfig _scene = new SceneMenuConfig();
	private HitResult _hover = HitResult.None;
	public event Action<string> HoverTextChanged;
	private HitResult _selected = HitResult.None;
	private string _backgroundPath = "";
	private Image? _backgroundImage;

	// --- SW static layer ---
	private Bitmap? _staticLayer;
	private int _staticLayerSignature;
	private int _iconGeneration = -1;

	// --- SW caches ---
	private bool _dragMode;
	private readonly Dictionary<AutoTextLayoutKey, RadialTextFitResult> _autoTextLayoutCache = new Dictionary<AutoTextLayoutKey, RadialTextFitResult>();
	private readonly Dictionary<FontKey, Font> _fontCache = new Dictionary<FontKey, Font>();
	private readonly RadialIconOverrides _customIcons = new RadialIconOverrides();
	private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);

	// --- Properties ---
	internal int AutoTextLayoutComputationCount { get; private set; }
	internal int AutoTextLayoutCacheCount => _autoTextLayoutCache.Count;
	internal int StaticLayerRenderCount { get; private set; }
	public bool EnableCellSelection { get; set; }
	public bool DrawBackdrop { get; set; } = true;
	public bool DrawForeground { get; set; } = true;
	public bool DrawGuides { get; set; } = true;
	public bool DrawLabels { get; set; } = true;
	public bool DrawHover { get; set; } = true;
	internal bool SurfaceOpacityAppliedByWindow { get; set; }

	// The CAD wheel marks the open centre of the icon wheel with a small cross.
	// Off by default: the popup surface is colour-keyed, and the settings
	// preview keeps its own centre caption instead. Only the live label layer
	// turns this on, so nothing that renders the control directly - including
	// the tests that pin the transparent centre - grows an opaque mark.
	public bool DrawCenterMark { get; set; }
	public bool ScaleMenuForPreview { get; set; }

	internal IReadOnlyList<TextLayoutDiagnostic> TextDiagnostics => _textDiagnostics;
	internal int TextLayoutCacheHits => _textLayoutCacheHits;
	internal int TextLayoutCacheMisses => _textLayoutCacheMisses;

	public event EventHandler<MenuItemConfig>? ItemSelected;
	public event EventHandler? CancelRequested;
	public event EventHandler<RadialCellSelectedEventArgs>? CellSelected;

	// --- Constructor ---
	public RadialMenuControl(Color transparentKey)
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = transparentKey;
		base.TabStop = true;
        _holdTimer.Tick += (_, _) => ArmCellDrag();
	}

	// --- SetMenu (with CAD reuseCache) ---
	internal void UseNonActivatingInput()
	{
		SetStyle(ControlStyles.Selectable, false);
		TabStop = false;
	}

	public void SetMenu(PluginConfig config, SceneMenuConfig scene, bool dragMode = false, bool reuseCache = false)
	{
		CancelCellDrag();
        if (!ReferenceEquals(_scene, scene) || !EnableCellSelection) _activeGroup = -1;
        int iconGeneration = RadialCommandIcons.Generation;
        var keepRenderCache = reuseCache && ReferenceEquals(_config, config) && ReferenceEquals(_scene, scene) && _iconGeneration == iconGeneration;
		_iconGeneration = iconGeneration;
		_config = config;
		_scene = scene;
		_dragMode = dragMode;
		_hover = HitResult.None;
		if (!EnableCellSelection)
			_selected = HitResult.None;
		if (!keepRenderCache)
		{
			_textLayoutCacheHits = 0;
			_textLayoutCacheMisses = 0;
		}
		// Native gesture guides use a plain annulus. Keep stored background
		// preferences intact; opening a wheel must never rewrite user settings.
		if (!keepRenderCache) { ReloadBackground(); _customIcons.Reload(); }
		if (!keepRenderCache) InvalidateStaticLayer();
		Invalidate();
	}

	// --- WarmRenderCache (CAD) ---
	internal void WarmRenderCache()
	{
		if (IsDisposed || Width <= 0 || Height <= 0) return;
		using var bitmap = new Bitmap(Width, Height);
		using var graphics = Graphics.FromImage(bitmap);
		using var args = new PaintEventArgs(graphics, new Rectangle(0, 0, Width, Height));
		OnPaint(args);
	}

	// --- WarmSharedRenderCache (CAD) ---
	internal static void WarmSharedRenderCache(PluginConfig config, SceneMenuConfig scene, Size size)
	{
		using var control = new RadialMenuControl(Color.FromArgb(1, 2, 3))
		{
			Size = size,
			DrawBackdrop = false,
			DrawGuides = false,
			DrawHover = false,
			DrawForeground = true,
			DrawLabels = true,
			ScaleMenuForPreview = false
		};
		control.SetMenu(config, scene);
		control.WarmRenderCache();
	}

	// --- UpdateCursor ---
	public void UpdateCursor(Point point)
	{
		if (!DrawHover)
			return;
		HitResult hitResult = HitTest(point);
		if (!_hover.Equals(hitResult))
		{
			_hover = hitResult;
			MenuItemConfig item = hitResult.HasSlot ? FindItem(hitResult) : null;
			string text = item?.Label ?? "";
			if (item?.Kind == ActionKinds.Combo) text += "（组合 · " + (item.ComboCommands?.Count ?? 0) + " 步）";
			AccessibleDescription = text;
			HoverTextChanged?.Invoke(text);
			Invalidate();
		}
	}

	// --- CommitAt ---
	public void CommitAt(Point point)
	{
		HitResult hit = HitTest(point);
        if (NavigateStyle(hit)) return;
		if (!hit.HasSlot)
		{
			Point point2 = point;
			Logger.Trace("Commit cancelled. No slot. Point=" + point2.ToString());
			this.CancelRequested?.Invoke(this, EventArgs.Empty);
			return;
		}
		MenuItemConfig menuItemConfig = FindItem(hit);
		if (menuItemConfig == null || string.IsNullOrWhiteSpace(menuItemConfig.Value))
		{
			Logger.Trace($"Commit cancelled. Empty slot. Point={point}, Ring={hit.RingIndex}, Slot={hit.Slot}");
			this.CancelRequested?.Invoke(this, EventArgs.Empty);
			return;
		}
		Logger.Trace($"Commit selected. Point={point}, Ring={hit.RingIndex}, Slot={hit.Slot}, Label={menuItemConfig.Label}, Kind={menuItemConfig.Kind}, Value={menuItemConfig.Value}");
		this.ItemSelected?.Invoke(this, menuItemConfig);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		TrackMagnifierPointer(e.Location);
		if (MoveGeometryDrag(e)) return;
        UpdateCellDrag(e);
		UpdateCursor(e.Location);
	}

	// The lens only has to repaint when it moves to another corner; a plain
	// move inside the same corner leaves both the lens and the hover untouched.
	private void TrackMagnifierPointer(Point point)
	{
		_magnifierPointer = point;
		int corner = (point.X < ClientSize.Width / 2 ? 0 : 1) + (point.Y < ClientSize.Height / 2 ? 0 : 2);
		if (_magnifierPointerActive && corner == _magnifierCorner) return;
		_magnifierPointerActive = true;
		_magnifierCorner = corner;
		if (PreviewMagnifierEnabled) Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			_magnifierMouseDown = true;
			if (PreviewMagnifierEnabled) Invalidate();
		}
		MouseButtons button = e.Button;
		if ((button == MouseButtons.Right || button == MouseButtons.Middle) ? true : false)
		{
			if (!_dragMode)
			{
				this.CancelRequested?.Invoke(this, EventArgs.Empty);
			}
		}
		else if (e.Button == MouseButtons.Left)
		{
			if (EnableCellSelection)
			{
				if (BeginGeometryDrag(e)) return;
				BeginCellDrag(e.Location);
			}
			else
			{
				CommitAt(e.Location);
			}
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (_magnifierMouseDown)
		{
			_magnifierMouseDown = false;
			if (PreviewMagnifierEnabled) Invalidate();
		}
        if (EnableCellSelection && e.Button == MouseButtons.Left)
		{
			if (EndGeometryDrag()) return;
			EndCellDrag(e.Location);
			return;
		}
		bool flag = !_dragMode;
		if (!flag)
		{
			MouseButtons button = e.Button;
			bool flag2 = ((button == MouseButtons.Right || button == MouseButtons.Middle) ? true : false);
			flag = !flag2;
		}
		if (!flag)
		{
			base.Capture = false;
			Logger.Trace("Overlay mouse released inside radial window at " + e.Location.ToString());
			CommitAt(e.Location);
		}
	}

	// --- OnPaint: SW structure with CAD SetClip ---
	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (ScaleMenuForPreview)
			e.Graphics.SetClip(ClientRectangle, CombineMode.Replace);
		ConfigureGraphics(e.Graphics);
		if (!DrawLabels || (!DrawHover && !EnableCellSelection && !ScaleMenuForPreview))
		{
			EnsureStaticLayer();
			if (_staticLayer != null)
			{
				e.Graphics.DrawImageUnscaled(_staticLayer, Point.Empty);
			}
			DrawInteractionHighlights(e.Graphics);
			return;
		}
		_textDiagnostics.Clear();
		DrawWheel(e.Graphics, includeHighlights: true);
	}

	// --- DrawWheel with CAD colors ---
	private void DrawWheel(Graphics graphics, bool includeHighlights)
	{
		// "classic" predates the alternative-style renderer and uses its own
		// sector geometry. The alternative renderer handled it as a smaller
		// "01", which made the wheel look wrong against the CAD build.
		if (_scene.StyleId == "classic") DrawClassicWheel(graphics, includeHighlights, highlightsOnly: false);
		else DrawStyledWheel(graphics, includeHighlights);
		// Editing overlays belong to the settings preview, never to the live
		// wheel, so they ride on the pass that also draws hover and selection.
		if (includeHighlights)
		{
			DrawInsertionPoints(graphics);
			DrawGeometryHandles(graphics);
			DrawCellDragFeedback(graphics);
			DrawPreviewMagnifier(graphics);
		}
	}

// --- SW static layer ---
	private void EnsureStaticLayer()
	{
		if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
			return;
		int signature = StaticLayerSignature();
		if (_staticLayer != null && _staticLayer.Size == ClientSize && _staticLayerSignature == signature)
			return;
		InvalidateStaticLayer();
		_staticLayer = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
		using Graphics graphics = Graphics.FromImage(_staticLayer);
		graphics.Clear(BackColor);
		ConfigureGraphics(graphics);
		_textDiagnostics.Clear();
		DrawWheel(graphics, includeHighlights: false);
		StaticLayerRenderCount++;
		_staticLayerSignature = signature;
	}

	private void DrawInteractionHighlights(Graphics graphics)
	{
		if (_scene.StyleId == "classic") { DrawClassicWheel(graphics, true, highlightsOnly: true); return; }
		DrawStyledWheel(graphics, true, true);
	}

	private void ConfigureGraphics(Graphics graphics)
	{
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = (!DrawBackdrop && !DrawGuides) ? TextRenderingHint.SingleBitPerPixelGridFit : TextRenderingHint.AntiAliasGridFit;
	}

	private int StaticLayerSignature()
	{
		unchecked
		{
			int hash = ClientSize.GetHashCode();
			hash = hash * 397 ^ BackColor.ToArgb();
			hash = hash * 397 ^ DrawBackdrop.GetHashCode();
			hash = hash * 397 ^ DrawForeground.GetHashCode();
			hash = hash * 397 ^ DrawGuides.GetHashCode();
			hash = hash * 397 ^ DrawLabels.GetHashCode();
			hash = hash * 397 ^ DrawCenterMark.GetHashCode();
			hash = hash * 397 ^ _iconGeneration;
			hash = hash * 397 ^ (_config.FontName ?? "").GetHashCode();
			hash = hash * 397 ^ _config.FontSizePx;
			hash = hash * 397 ^ _config.FontBold.GetHashCode();
			hash = hash * 397 ^ (_config.TextEffect ?? "").GetHashCode();
			hash = hash * 397 ^ (_config.TextColor ?? "").GetHashCode();
			hash = hash * 397 ^ EffectiveMenuSizePx();
			hash = hash * 397 ^ EffectiveCenterRadiusPx();
			hash = hash * 397 ^ (_scene.BackgroundImagePath ?? "").GetHashCode();
			hash = hash * 397 ^ _scene.BackgroundOpacityPercent;
            hash = hash * 397 ^ (_scene.StyleId ?? "01").GetHashCode();
            hash = hash * 397 ^ (_scene.IconColor ?? "Green").GetHashCode();
            hash = hash * 397 ^ (_scene.ContentMode ?? "").GetHashCode();
            hash = hash * 397 ^ _scene.EqualRingBands.GetHashCode();
            hash = hash * 397 ^ _activeGroup;
			// The cached layer is a bitmap of the whole surface, so anything the
			// appearance or the per-cell shape/font contributes must be part of
			// the signature or the preview keeps showing the previous look.
			hash = hash * 397 ^ SurfaceAlpha;
			hash = hash * 397 ^ SurfaceOpacityAppliedByWindow.GetHashCode();
			WheelAppearance appearance = Wheel;
			hash = hash * 397 ^ (appearance.PresetId ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.FillMode ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.StartColor ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.EndColor ?? "").GetHashCode();
			hash = hash * 397 ^ appearance.GradientAngle;
			hash = hash * 397 ^ (appearance.BorderColor ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.HoverColor ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.SelectedColor ?? "").GetHashCode();
			hash = hash * 397 ^ (appearance.TextColor ?? "").GetHashCode();
			hash = hash * 397 ^ appearance.Glass.GetHashCode();
			hash = hash * 397 ^ appearance.ShadowPercent;
			foreach (RingConfig ring in _scene.Rings.Where(item => item.Enabled).OrderBy(item => item.Index))
			{
				hash = hash * 397 ^ ring.Index;
				hash = hash * 397 ^ ring.Slots;
				hash = hash * 397 ^ ring.DiameterPx;
				hash = hash * 397 ^ ring.SizeWeight;
				hash = hash * 397 ^ ring.NodeInnerSizePercent;
				hash = hash * 397 ^ ring.NodeOuterSizePercent;
				hash = hash * 397 ^ (ring.Name ?? "").GetHashCode();
				foreach (MenuItemConfig cell in ring.Items)
				{
					hash = hash * 397 ^ (cell.CellShape ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.ResolvedFontFamily() ?? "").GetHashCode();
					hash = hash * 397 ^ cell.Slot;
					hash = hash * 397 ^ cell.CellSizePercent;
					hash = hash * 397 ^ cell.IconRatioPercent;
					hash = hash * 397 ^ cell.FreeX.GetHashCode();
					hash = hash * 397 ^ cell.FreeY.GetHashCode();
					hash = hash * 397 ^ cell.FontSizePx;
					hash = hash * 397 ^ cell.FontBold.GetHashCode();
					hash = hash * 397 ^ (cell.Label ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.Kind ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.Value ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.IconId ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.IconData ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.IconPath ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.ImageMode ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.ContentMode ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.TextEffect ?? "").GetHashCode();
					hash = hash * 397 ^ (cell.TextColor ?? "").GetHashCode();
					foreach (var step in cell.ComboCommands ?? Enumerable.Empty<ComboCommandConfig>())
					{
						if (step == null) continue;
						hash = hash * 397 ^ (step.Kind ?? "").GetHashCode();
						hash = hash * 397 ^ (step.Value ?? "").GetHashCode();
					}
				}
			}
			return hash;
		}
	}

	private void InvalidateStaticLayer()
	{
		_staticLayer?.Dispose();
		_staticLayer = null;
		_staticLayerSignature = 0;
	}

	// --- SelectCell ---
	public void SelectCell(int ringIndex, int slot)
	{
		if (!EnableCellSelection)
			return;
		RingConfig ring = _scene.Rings.FirstOrDefault(item => item.Index == ringIndex && item.Enabled);
		if (ring == null || slot < 0 || slot >= ring.Slots)
			return;
		_selected = new HitResult(ringIndex, slot, center: false);
		_hover = _selected;
		Invalidate();
		CellSelected?.Invoke(this, new RadialCellSelectedEventArgs(ringIndex, slot));
	}

	// The classic centre follows the CAD wheel: a raised disc with a hairline
	// rim, and a "取消" caption that marks it as the cancel target. The live
	// gesture window draws neither the disc nor a caption over the model
	// (DrawGuides is off there), exactly as it is in the CAD build.
	private void DrawCenter(Graphics g, RectangleF bounds, bool includeHighlights)
	{
		PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
		int centerRadius = EffectiveCenterRadiusPx();
		RectangleF rect = RectAround(center, centerRadius);
		bool hovered = includeHighlights && _hover.IsCenter;
		if (DrawGuides)
		{
			using Brush fill = new SolidBrush(hovered ? Color.FromArgb(234, 244, 248) : Wheel.GetSurfaceColor());
			g.FillEllipse(fill, rect);
			using Pen pen = new Pen(hovered ? Wheel.GetHoverColor() : Wheel.GetBorderColor(), hovered ? 1.8f : 1.2f);
			g.DrawEllipse(pen, rect);
		}
		if (!DrawLabels) return;
		float size = Math.Max(11f, Math.Min(22f, centerRadius * 0.66f));
		using Font font = CreateWheelFont(_config.FontName, size, _config.FontBold);
		TextRenderer.DrawText(g, "\u53d6\u6d88", font, Rectangle.Round(rect),
			hovered ? Wheel.GetHoverColor() : WheelTextColor,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
			TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
    }

	// --- ReloadBackground (with force param) ---
	private void ReloadBackground(bool force = false)
	{
		var path = (_scene.BackgroundImagePath ?? "").Trim();
		if (!force && path.Equals(_backgroundPath, StringComparison.OrdinalIgnoreCase))
			return;
		_backgroundPath = path;
		InvalidateStaticLayer();
		_backgroundImage?.Dispose();
		_backgroundImage = null;
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			return;
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var source = Image.FromStream(stream);
			_backgroundImage = new Bitmap(source);
		}
		catch (Exception ex)
		{
			Logger.Error("Load radial background failed: " + path, ex);
		}
	}

	protected override void OnPaintBackground(PaintEventArgs pevent)
	{
		pevent.Graphics.Clear(BackColor);
	}

	protected override void OnSizeChanged(EventArgs e)
	{
		InvalidateStaticLayer();
		if (ScaleMenuForPreview)
		{
			SharedTextLayoutCache.Clear();
		}
		base.OnSizeChanged(e);
	}

	protected override void OnBackColorChanged(EventArgs e)
	{
		InvalidateStaticLayer();
		base.OnBackColorChanged(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_holdTimer.Dispose();
            _customIcons.Dispose();
			_backgroundImage?.Dispose();
			_backgroundImage = null;
			InvalidateStaticLayer();
			DisposeHitGeometry();
			_autoTextLayoutCache.Clear();
			DisposeFontCache();
			foreach (var icon in _iconCache.Values) icon.Dispose();
			_iconCache.Clear();
		}
		base.Dispose(disposing);
	}

	// --- HitTest ---
	private HitResult HitTest(Point point) => HitStyledWheel(point);

private void SelectCellAt(Point point)
	{
		HitResult hover = HitTest(point);
		if (hover.HasSlot)
		{
			SelectCell(hover.RingIndex, hover.Slot);
		}
	}

	private MenuItemConfig? FindItem(HitResult hit)
	{
		return _scene.Rings.FirstOrDefault((RingConfig r) => r.Index == hit.RingIndex)?.Items.FirstOrDefault((MenuItemConfig i) => i.Slot == hit.Slot);
	}

	private RectangleF GetMenuBounds()
	{
		int num = DisplayMenuSizePx();
		return new RectangleF((float)(base.Width - num) / 2f, (float)(base.Height - num) / 2f, num, num);
	}

	private int DisplayMenuSizePx()
	{
		int available = Math.Max(120, Math.Min(base.Width, base.Height) - (ScaleMenuForPreview ? 16 : 0));
		if (!ScaleMenuForPreview)
		{
			return Math.Min(available, Math.Max(120, EffectiveMenuSizePx()));
		}
		// A preview only fills its pane when the wheel itself is configured large.
		// Filling it unconditionally made a 260 px wheel look enormous next to the
		// CAD build, which scales the preview by the configured size instead.
		float ratio = Math.Max(0f, Math.Min(1f, (LogicalMenuSizePx() - 260f) / (900f - 260f)));
		float previewRatio = .86f + ratio * .14f;
		return Math.Max(120, Math.Min(available, (int)Math.Round(available * previewRatio)));
	}

	private static RectangleF RectAround(PointF center, float radius)
	{
		return new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
	}

	private static RectangleF Cover(RectangleF bounds, Size imageSize)
	{
		if (imageSize.Width <= 0 || imageSize.Height <= 0)
		{
			return bounds;
		}
		float num = Math.Max(bounds.Width / (float)imageSize.Width, bounds.Height / (float)imageSize.Height);
		float num2 = (float)imageSize.Width * num;
		float num3 = (float)imageSize.Height * num;
		return new RectangleF(bounds.Left + (bounds.Width - num2) / 2f, bounds.Top + (bounds.Height - num3) / 2f, num2, num3);
	}

	private List<RingLayout> BuildRingLayouts(RectangleF bounds, List<RingConfig> rings)
	{
		List<RingLayout> list = new List<RingLayout>();
		float outerRadius = bounds.Width / 2f;
		float innerRadius = Math.Max(1f, EffectiveCenterRadiusPx());
		// Same display-only widening the CAD wheel applies: a lone icon ring
		// reads as a broad transparent hub with a compact band, instead of one
		// ring stretched from a tiny hub to the rim. The saved centre radius is
		// never rewritten, and the hit test shares this builder, so pressing in
		// the open middle still cancels.
		innerRadius = AdjustSwInnerRadius(innerRadius, bounds, rings.Count);
		float centerRadius = innerRadius;
		float scale = bounds.Width / Math.Max(1f, LogicalMenuSizePx());
		int totalWeight = Math.Max(1, rings.Sum((RingConfig ring) => Math.Max(1, ring.SizeWeight)));
		int cumulativeWeight = 0;
		for (int index = 0; index < rings.Count; index++)
		{
			RingConfig ring = rings[index];
			cumulativeWeight += Math.Max(1, ring.SizeWeight);
			float configuredOuter = ring.DiameterPx > 0
				? ring.DiameterPx * 0.5f * scale
				: centerRadius + (outerRadius - centerRadius) * cumulativeWeight / totalWeight;
			// A stored diameter that predates a hub change must never collapse
			// a ring into a sliver; the last ring always closes on the wheel.
			float nextOuter = index == rings.Count - 1
				? outerRadius
				: Math.Min(outerRadius, Math.Max(innerRadius + 8f, configuredOuter));
			// Node size percentages grow or shrink the node about the middle of
			// its band, so the ring keeps its centre line.
			float mid = (innerRadius + nextOuter) / 2f;
			float nodeInner = mid - (mid - innerRadius) * Math.Max(0.4f, Math.Min(1.6f, ring.NodeInnerSizePercent > 0 ? ring.NodeInnerSizePercent / 100f : 1f));
			float nodeOuter = mid + (nextOuter - mid) * Math.Max(0.4f, Math.Min(1.6f, ring.NodeOuterSizePercent > 0 ? ring.NodeOuterSizePercent / 100f : 1f));
			if (nodeOuter - nodeInner < 2f)
			{
				nodeInner = innerRadius;
				nodeOuter = nextOuter;
			}
			list.Add(new RingLayout(ring, nodeInner, nodeOuter));
			innerRadius = nextOuter;
		}
		return list;
	}

	private int EffectiveMenuSizePx()
	{
		int size = _scene.MenuSizePx > 0 ? _scene.MenuSizePx : _config.MenuSizePx;
		return ComputeEffectiveMenuSizePx(size, base.DeviceDpi);
	}

	private int LogicalMenuSizePx() => ComputeEffectiveMenuSizePx(_scene.MenuSizePx > 0 ? _scene.MenuSizePx : _config.MenuSizePx, 96);

	// The saved MenuSizePx is in logical pixels (what the user typed). At higher
	// system DPI the wheel needs more physical pixels for fonts/icons/geometry
	// to stay the same visual size. Headless tests run at 96 DPI so this is a
	// no-op for them; 144/192 DPI screens get a 1.5x/2x scale-up. The 260..900
	// clamp is applied to the saved logical size only — scaling up to physical
	// pixels afterwards is the whole point of DPI awareness, so a 192 DPI
	// display legitimately asks for a 1100 px wheel even though the saved
	// value is "550".
	public static int ComputeEffectiveMenuSizePx(int savedMenuSizePx, int deviceDpi)
	{
		int size = Math.Max(260, Math.Min(900, savedMenuSizePx));
		float dpiScale = Math.Max(1f, deviceDpi / 96f);
		return (int)Math.Round(size * dpiScale);
	}

	internal float DpiScale() => Math.Max(1f, base.DeviceDpi / 96f);

	public static float ComputeDpiScale(int deviceDpi) => Math.Max(1f, deviceDpi / 96f);

	// The single-ring icon wheel needs a broad transparent centre. This is
	// display geometry only: the saved centre radius is never rewritten.
	private float AdjustSwInnerRadius(float original, RectangleF bounds, int ringCount) =>
		string.Equals(_scene.StyleId, "01", StringComparison.OrdinalIgnoreCase) && ringCount == 1
			? Math.Max(original, bounds.Width * .29f) : original;

	private int EffectiveCenterRadiusPx()
	{
        int menuSize = LogicalMenuSizePx();
        // Same helper the ring layout uses, so the hub the renderer leaves open
        // is exactly the hub the diameters were distributed from. Measuring the
        // hub from ring 1 instead used to make the innermost ring narrower than
        // every other ring.
        int radius = RingDiameterLayout.EffectiveCenterRadius(_scene, menuSize, _config.CenterRadiusPx);
        return Math.Max(8, (int)Math.Round(radius * (double)DisplayMenuSizePx() / menuSize));
	}

	private static void DrawCircle(Graphics g, Pen pen, PointF center, float radius)
	{
		g.DrawEllipse(pen, RectAround(center, radius));
	}

	private static void FillSlot(Graphics g, Brush brush, PointF center, float inner, float outer, float start, float sweep)
	{
		using GraphicsPath graphicsPath = CreateSlotPath(center, inner, outer, start, sweep);
		g.FillPath(brush, graphicsPath);
	}

	internal static GraphicsPath CreateSlotPath(PointF center, float inner, float outer, float start, float sweep)
	{
		GraphicsPath path = new GraphicsPath();
		inner = Math.Max(0.1f, inner);
		outer = Math.Max(inner + 0.1f, outer);
		path.AddArc(RectAround(center, outer), start, sweep);
		path.AddArc(RectAround(center, inner), start + sweep, 0f - sweep);
		path.CloseFigure();
		return path;
	}

	private static PointF PointOnCircle(PointF center, float radius, float degrees)
	{
		double num = (double)degrees * Math.PI / 180.0;
		return new PointF(center.X + (float)Math.Cos(num) * radius, center.Y + (float)Math.Sin(num) * radius);
	}

	// ===== DrawFittedText (merged: SW auto-fit + CAD fixed/wrap + CAD rendering) =====
	private void DrawFittedText(Graphics graphics, string label, Color color, bool fontBold, string textEffect, PointF center, float radius, float ringWidth, int slots, int itemFontSizePx, string fontFamily, GraphicsPath cellPath, PointF wheelCenter, float innerRadius, float outerRadius, float cellAngle)
	{
		label = (label ?? "").Trim();
		if (label.Length == 0) return;

		textEffect = TextEffects.Normalize(textEffect);
		var effectPadding = TextEffects.OverflowPadding(textEffect);

		var sweepRadians = Math.PI * 2.0 / Math.Max(1, slots);
		var pathBounds = cellPath.GetBounds();
		float availableWidth = Math.Max(10f, Math.Min(pathBounds.Width * 0.96f,
			2f * Math.Max(10f, radius) * (float)Math.Sin(sweepRadians / 2.0) * 0.96f));
		float availableHeight = Math.Max(10f, Math.Min(pathBounds.Height * 0.96f, ringWidth * 0.94f));
		availableWidth = Math.Max(10f, availableWidth - effectPadding * 2f);
		availableHeight = Math.Max(10f, availableHeight - effectPadding * 2f);

		float previewScale = ScaleMenuForPreview
			? DisplayMenuSizePx() / (float)Math.Max(1, EffectiveMenuSizePx())
			: 1f;

		string fontName = string.IsNullOrWhiteSpace(fontFamily) ? _config.FontName : fontFamily.Trim();
		bool autoFit = itemFontSizePx <= 0;

		string[] lines = new[] { label };
		Font font = null;
		SizeF textSize = SizeF.Empty;
		bool fullText = false;
		PointF textCenter = center;
		RectangleF[] fittedLineBounds = null;

		if (!autoFit)
		{
			// Fixed font-size path: use CAD's WrapText (with TokenizeForWrap) at maxSize
			float maxSize = RadialTextLayout.MaximumFontSize(itemFontSizePx, previewScale, availableHeight);
			font = GetCachedFont(fontName, maxSize);
			lines = WrapText(graphics, label, font, availableWidth, Math.Max(1, label.Length), out fullText);
			textSize = MeasureLines(graphics, lines, font);
			if (fullText && textSize.Width <= availableWidth && textSize.Height <= availableHeight)
			{
				TryPlaceText(cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
					textSize, font.Size, out textCenter);
			}
			else if (fullText)
			{
				// Fixed-size overflow guard: a literal 24 px on a 16-slot ring band
				// simply doesn't fit. Shrink the font (keep the user's preference
				// as the upper bound, scale down) before letting the clipping
				// path chop characters off. If the shrunk font still doesn't fit
				// after rewrap, give up — fullText=false falls through to the
				// ellipsis path below.
				float shrunkSize = RadialTextLayout.ShrinkFixedFontSizeToFit(maxSize, textSize, availableWidth, availableHeight);
				if (shrunkSize + 0.5f < maxSize)
				{
					Font smallFont = GetCachedFont(fontName, shrunkSize);
					string[] shrunkLines = WrapText(graphics, label, smallFont, availableWidth, Math.Max(1, label.Length), out bool shrunkFull);
					SizeF shrunkSizeF = MeasureLines(graphics, shrunkLines, smallFont);
					if (shrunkFull && shrunkSizeF.Width <= availableWidth && shrunkSizeF.Height <= availableHeight)
					{
						font.Dispose();
						font = smallFont;
						lines = shrunkLines;
						textSize = shrunkSizeF;
						fullText = true;
						TryPlaceText(cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
							textSize, font.Size, out textCenter);
					}
					else
					{
						smallFont.Dispose();
						fullText = false;
					}
				}
				else
				{
					fullText = false;
				}
			}
		}
		else
		{
			// Auto-fit path: SW's GetCachedAutoTextLayout
			RadialTextFitResult fit = GetCachedAutoTextLayout(graphics, label, fontName, cellPath, center, radius, cellAngle, ringWidth, availableWidth, availableHeight, previewScale);
			label = fit.DisplayText;
			lines = fit.Lines;
			textSize = fit.TextSize;
			fullText = fit.FullText;
			textCenter = fit.Center;
			fittedLineBounds = fit.LineBounds;
			font = GetCachedFont(fontName, fit.FontSize);
		}

		if (font == null) return;

		// Render using CAD's DrawStyledText
		float lineHeight = MeasureTextHeight(graphics, font);
		float y = textCenter.Y - (lineHeight * lines.Length) / 2f;
		var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
			TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;
		var state = graphics.Save();
		graphics.SetClip(cellPath, CombineMode.Intersect);
		for (int i = 0; i < lines.Length; i++)
		{
			Rectangle bounds;
			if (fittedLineBounds != null && i < fittedLineBounds.Length && !fittedLineBounds[i].IsEmpty)
			{
				bounds = Rectangle.Round(fittedLineBounds[i]);
			}
			else
			{
				bounds = Rectangle.Round(new RectangleF(textCenter.X - availableWidth / 2f, y + i * lineHeight, availableWidth, lineHeight + 1f));
			}
			DrawStyledText(graphics, lines[i], font, bounds, color, textEffect, flags);
		}
		graphics.Restore(state);
	}

	// ===== SW auto-text methods =====

	private RadialTextFitResult GetCachedAutoTextLayout(Graphics graphics, string label, string fontName, GraphicsPath cellPath, PointF middleCenter, float radius, float angleDegrees, float ringWidth, float availableWidth, float availableHeight, float previewScale)
	{
		AutoTextLayoutKey key = new AutoTextLayoutKey(label, fontName, middleCenter, radius, ringWidth, availableWidth, availableHeight, angleDegrees, previewScale, graphics);
		if (_autoTextLayoutCache.TryGetValue(key, out RadialTextFitResult cached))
		{
			return cached;
		}
		if (_autoTextLayoutCache.Count >= 512)
		{
			_autoTextLayoutCache.Clear();
		}
		RadialTextFitResult fitted = FitAutoTextInCell(graphics, label, fontName, cellPath, middleCenter, angleDegrees, ringWidth, availableWidth, availableHeight, previewScale);
		_autoTextLayoutCache[key] = fitted;
		AutoTextLayoutComputationCount++;
		return fitted;
	}

	private Font GetCachedFont(string fontName, float size)
	{
		FontKey key = new FontKey(fontName, size);
		if (_fontCache.TryGetValue(key, out Font cached))
		{
			return cached;
		}
		if (_fontCache.Count >= 128)
		{
			DisposeFontCache();
		}
		Font font = CreateWheelFont(fontName, size);
		_fontCache[key] = font;
		return font;
	}

	private void DisposeFontCache()
	{
		foreach (Font font in _fontCache.Values)
		{
			font.Dispose();
		}
		_fontCache.Clear();
	}

	internal static RadialTextFitResult FitAutoTextInCell(Graphics graphics, string label, string fontName, GraphicsPath cellPath, PointF middleCenter, float angleDegrees, float ringWidth, float availableWidth, float availableHeight, float previewScale)
	{
		string displayText = CompactAutoLabel(label);
		float maxSize = RadialTextLayout.MaximumFontSize(0, previewScale, availableHeight);
		float readableMin = RadialTextLayout.MinimumReadableFontSize(previewScale, maxSize);
		int preferredLines = RadialTextLayout.MaximumAutoLines(displayText.Length);
		float fallbackMin = Math.Min(readableMin, RadialTextLayout.MinimumFallbackFontSize(previewScale));
		int low = Math.Max(1, (int)Math.Ceiling(fallbackMin * 2f));
		int high = Math.Max(low, (int)Math.Floor(maxSize * 2f));
		RadialTextFitResult best = null;
		while (low <= high)
		{
			int middle = low + (high - low) / 2;
			float size = middle * 0.5f;
			int lineLimit = size >= readableMin ? preferredLines : 6;
			RadialTextFitResult? fit = TryFitAutoTextInPath(graphics, displayText, fontName, cellPath, middleCenter, angleDegrees, ringWidth, size, lineLimit);
			if (fit != null)
			{
				best = fit;
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}
		if (best != null)
		{
			return best;
		}
		RadialTextFitResult fallback = FitAutoText(graphics, displayText, fontName, availableWidth, availableHeight, previewScale);
		return new RadialTextFitResult(fallback.DisplayText, fallback.Lines, fallback.FontSize, fallback.TextSize, fallback.FullText, middleCenter);
	}

	private static RadialTextFitResult? TryFitAutoTextInPath(Graphics graphics, string text, string fontName, GraphicsPath cellPath, PointF middleCenter, float angleDegrees, float ringWidth, float fontSize, int maxLines)
	{
		using Font font = CreateWheelFont(fontName, fontSize);
		int lineLimit = Math.Max(1, Math.Min(Math.Min(maxLines, 6), text.Length));
		float lineHeight = font.GetHeight(graphics) * 0.92f;
		float margin = Math.Max(2f, fontSize * 0.08f);
		RectangleF pathBounds = cellPath.GetBounds();
		float usableWidth = Math.Max(1f, pathBounds.Width - margin * 2f);
		float fullWidth = graphics.MeasureString(text, font).Width;
		int minimumLines = Math.Max(1, Math.Min(lineLimit, (int)Math.Ceiling(fullWidth / usableWidth)));
		double radians = angleDegrees * Math.PI / 180.0;
		float dx = (float)Math.Cos(radians);
		float dy = (float)Math.Sin(radians);
		float[] offsets = RadialOffsets();
		for (int lineCount = minimumLines; lineCount <= lineLimit; lineCount++)
		{
			if (lineHeight * lineCount + margin * 2f > pathBounds.Height)
			{
				continue;
			}
			string[] lines = SplitBalanced(text, lineCount);
			if (lines.Any(line => graphics.MeasureString(line, font).Width + margin * 2f > pathBounds.Width))
			{
				continue;
			}
			foreach (float offset in offsets)
			{
				PointF candidate = new PointF(middleCenter.X + dx * ringWidth * offset, middleCenter.Y + dy * ringWidth * offset);
				RectangleF[] lineBounds = MeasureLineBounds(graphics, lines, font, candidate, lineHeight);
				if (lineBounds.All(bounds => RectangleInsidePath(cellPath, bounds, margin, pathBounds)))
				{
					SizeF textSize = new SizeF(lineBounds.Max(bounds => bounds.Width), lineHeight * lines.Length);
					return new RadialTextFitResult(text, lines, fontSize, textSize, fullText: true, candidate, lineBounds);
				}
			}
		}
		return null;
	}

	private static float[] RadialOffsets()
	{
		List<float> offsets = new List<float> { 0f };
		for (float offset = 0.06f; offset <= 0.48f; offset += 0.06f)
		{
			offsets.Add(0f - offset);
			offsets.Add(offset);
		}
		return offsets.ToArray();
	}

	private static RectangleF[] MeasureLineBounds(Graphics graphics, string[] lines, Font font, PointF center, float lineHeight)
	{
		RectangleF[] bounds = new RectangleF[lines.Length];
		float top = center.Y - lineHeight * lines.Length * 0.5f;
		for (int index = 0; index < lines.Length; index++)
		{
			float width = Math.Max(1f, graphics.MeasureString(lines[index], font).Width);
			bounds[index] = new RectangleF(center.X - width * 0.5f, top + index * lineHeight, width, lineHeight);
		}
		return bounds;
	}

	internal static bool LineBoundsInsidePath(GraphicsPath path, RectangleF[] lineBounds, float margin)
	{
		RectangleF pathBounds = path.GetBounds();
		return lineBounds != null && lineBounds.Length > 0 && lineBounds.All(bounds => RectangleInsidePath(path, bounds, margin, pathBounds));
	}

	private static string[] SplitBalanced(string text, int lineCount)
	{
		string value = (text ?? "").Replace("\r", "").Replace("\n", "");
		lineCount = Math.Max(1, Math.Min(lineCount, Math.Max(1, value.Length)));
		List<string> lines = new List<string>(lineCount);
		int position = 0;
		for (int line = 0; line < lineCount; line++)
		{
			int remainingCharacters = value.Length - position;
			int remainingLines = lineCount - line;
			int take = (int)Math.Ceiling(remainingCharacters / (double)remainingLines);
			lines.Add(value.Substring(position, take));
			position += take;
		}
		return lines.ToArray();
	}

	// SW 5x5 RectangleInsidePath (used by auto-text fitting)
	internal static bool RectangleInsidePath(GraphicsPath path, RectangleF bounds, float margin)
	{
		return RectangleInsidePath(path, bounds, margin, path.GetBounds());
	}

	private static bool RectangleInsidePath(GraphicsPath path, RectangleF bounds, float margin, RectangleF pathBounds)
	{
		RectangleF test = bounds;
		test.Inflate(Math.Max(0f, margin), Math.Max(0f, margin));
		if (!pathBounds.Contains(test))
		{
			return false;
		}
		const int samples = 5;
		for (int y = 0; y < samples; y++)
		{
			float py = test.Top + test.Height * y / (samples - 1f);
			for (int x = 0; x < samples; x++)
			{
				float px = test.Left + test.Width * x / (samples - 1f);
				if (!path.IsVisible(px, py))
				{
					return false;
				}
			}
		}
		return true;
	}

	internal static RadialTextFitResult FitAutoText(Graphics graphics, string label, string fontName, float availableWidth, float availableHeight, float previewScale)
	{
		string displayText = CompactAutoLabel(label);
		float maxSize = RadialTextLayout.MaximumFontSize(0, previewScale, availableHeight);
		float readableMin = RadialTextLayout.MinimumReadableFontSize(previewScale, maxSize);
		RadialTextFitResult last = null;
		int preferredLines = RadialTextLayout.MaximumAutoLines(displayText.Length);
		for (float size = maxSize; size >= readableMin; size -= 0.5f)
		{
			last = MeasureAutoCandidate(graphics, displayText, fontName, size, availableWidth, availableHeight, preferredLines);
			if (Fits(last, availableWidth, availableHeight))
			{
				return last;
			}
		}
		float fallbackMin = Math.Min(readableMin, Math.Max(1f, 12f * Math.Max(0.05f, previewScale)));
		for (float size = Math.Min(maxSize, readableMin - 0.5f); size >= fallbackMin; size -= 0.5f)
		{
			last = MeasureAutoCandidate(graphics, displayText, fontName, size, availableWidth, availableHeight, 6);
			if (Fits(last, availableWidth, availableHeight))
			{
				return last;
			}
		}
		return last ?? new RadialTextFitResult(displayText, new[] { displayText }, 1f, SizeF.Empty, fullText: true);
	}

	private static RadialTextFitResult MeasureAutoCandidate(Graphics graphics, string text, string fontName, float fontSize, float availableWidth, float availableHeight, int lineLimit)
	{
		using Font font = CreateWheelFont(fontName, fontSize);
		int maxLines = Math.Max(1, Math.Min(lineLimit, MaxTextLines(graphics, font, availableHeight)));
		string[] lines = WrapText(graphics, text, font, availableWidth, maxLines, out bool fullText);
		return new RadialTextFitResult(text, lines, fontSize, MeasureLines(graphics, lines, font), fullText);
	}

	private static bool Fits(RadialTextFitResult result, float availableWidth, float availableHeight)
	{
		return result.FullText && result.TextSize.Width <= availableWidth && result.TextSize.Height <= availableHeight;
	}

	private static string CompactAutoLabel(string label)
	{
		string compact = (label ?? "").Trim().TrimEnd('.', '…').Trim();
		compact = compact.Replace("[", "").Replace("]", "");
		return compact.Length == 0 ? (label ?? "").Trim() : compact;
	}

	// ===== CAD text methods =====

	private static bool TryBuildTextLayoutAtSize(Graphics graphics, string label, string fontFamily, bool fontBold,
		float fontSize, float availableWidth, float availableHeight, GraphicsPath cellPath,
		PointF wheelCenter, float innerRadius, float outerRadius, float cellAngle,
		out string[] lines, out SizeF textSize, out bool fullText, out PointF placedCenter)
	{
		using var font = CreateWheelFont(fontFamily, fontSize, fontBold);
		lines = WrapText(graphics, label, font, availableWidth,
			MaxTextLines(graphics, font, availableHeight), out fullText);
		if (fullText) lines = BalanceLines(graphics, label, lines, font, availableWidth);
		textSize = MeasureLines(graphics, lines, font);
		if (!fullText || textSize.Width > availableWidth || textSize.Height > availableHeight)
		{
			placedCenter = PointF.Empty;
			return false;
		}
		return TryPlaceText(cellPath, wheelCenter, innerRadius, outerRadius, cellAngle,
			textSize, font.Size, out placedCenter);
	}

	private static string BuildTextLayoutCacheKey(string label, string fontFamily, bool fontBold, string textEffect, int itemFontSizePx, int slots,
		float previewScale, float availableWidth, float availableHeight, PointF wheelCenter,
		float innerRadius, float outerRadius, float cellAngle)
	{
		return string.Join("\u001F", label, fontFamily ?? "", fontBold ? "bold" : "regular", TextEffects.Normalize(textEffect), itemFontSizePx.ToString(CultureInfo.InvariantCulture),
			slots.ToString(CultureInfo.InvariantCulture), CacheNumber(previewScale), CacheNumber(availableWidth),
			CacheNumber(availableHeight), CacheNumber(wheelCenter.X), CacheNumber(wheelCenter.Y),
			CacheNumber(innerRadius), CacheNumber(outerRadius), CacheNumber(cellAngle));
	}

	private static string CacheNumber(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	// CAD 5-point RectangleInsidePath (used by TryPlaceText)
	private static bool RectangleInsidePath(GraphicsPath path, RectangleF bounds)
	{
		if (!path.IsVisible(bounds.Left, bounds.Top)
			|| !path.IsVisible(bounds.Right, bounds.Top)
			|| !path.IsVisible(bounds.Left, bounds.Bottom)
			|| !path.IsVisible(bounds.Right, bounds.Bottom)
			|| !path.IsVisible(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f))
			return false;
		return true;
	}

	private static bool TryPlaceText(GraphicsPath cellPath, PointF wheelCenter, float innerRadius, float outerRadius,
		float angle, SizeF textSize, float fontSize, out PointF placedCenter)
	{
		var padding = Math.Max(1.2f, fontSize * 0.12f);
		var width = textSize.Width + padding * 2f;
		var height = textSize.Height + padding * 2f;
		foreach (var fraction in TextPlacementFractions)
		{
			var radius = innerRadius + Math.Max(0f, outerRadius - innerRadius) * fraction;
			var candidate = PointOnCircle(wheelCenter, radius, angle);
			var bounds = new RectangleF(candidate.X - width / 2f, candidate.Y - height / 2f, width, height);
			if (!RectangleInsidePath(cellPath, bounds)) continue;
			placedCenter = candidate;
			return true;
		}
		placedCenter = PointF.Empty;
		return false;
	}

	// --- DrawStyledText (CAD) ---
	private static void DrawStyledText(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
		string effect, TextFormatFlags flags)
	{
		effect = TextEffects.Normalize(effect);
		if (effect == TextEffects.None)
		{
			DrawPlainText(graphics, text, font, bounds, color);
			return;
		}
		if (effect == TextEffects.Shadow || effect == TextEffects.WordArt)
			TextRenderer.DrawText(graphics, text, font, Offset(bounds, 2, 2), Color.FromArgb(145, 0, 0, 0), flags);
		if (effect == TextEffects.Outline || effect == TextEffects.WordArt)
		{
			var outline = IsLightColor(color) ? Color.FromArgb(185, 20, 28, 36) : Color.FromArgb(185, 255, 255, 255);
			foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
				TextRenderer.DrawText(graphics, text, font, Offset(bounds, offset.X, offset.Y), outline, flags);
		}
		TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
	}

	private static void DrawPlainText(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
	{
		var previousHint = graphics.TextRenderingHint;
		graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
		using var brush = new SolidBrush(color);
		using var format = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
		};
		graphics.DrawString(text, font, brush, bounds, format);
		graphics.TextRenderingHint = previousHint;
	}

	// --- CreateWheelFont (CAD: GraphicsUnit.Pixel, "宋体") ---
	private static Font CreateWheelFont(string fontFamily, float size, bool bold = false)
	{
		var style = bold ? FontStyle.Bold : FontStyle.Regular;
		try
		{
			return new Font(string.IsNullOrWhiteSpace(fontFamily) ? "宋体" : fontFamily.Trim(), size, style, GraphicsUnit.Pixel);
		}
		catch
		{
			try { return new Font("宋体", size, style, GraphicsUnit.Pixel); }
			catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
		}
	}

	// --- MeasureTextWidth (CAD: TextRenderer) ---
	private static float MeasureTextWidth(Graphics graphics, string text, Font font)
	{
		var size = TextRenderer.MeasureText(graphics, text ?? "", font, new Size(32767, 32767),
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
		return size.Width;
	}

	// --- MeasureTextHeight (CAD: TextRenderer) ---
	private static float MeasureTextHeight(Graphics graphics, Font font)
	{
		return TextRenderer.MeasureText(graphics, "国", font, new Size(32767, 32767),
			TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
	}

	// --- MaxTextLines (CAD: TextRenderer-based) ---
	private static int MaxTextLines(Graphics graphics, Font font, float availableHeight)
	{
		float lineHeight = Math.Max(1f, MeasureTextHeight(graphics, font));
		return Math.Max(1, Math.Min(24, (int)Math.Floor(availableHeight / lineHeight)));
	}

	// --- WrapText (CAD: with TokenizeForWrap) ---
	private static string[] WrapText(Graphics graphics, string text, Font font, float availableWidth, int maxLines, out bool fullText, bool trimWithEllipsis = false)
	{
		List<string> lines = new List<string>();
		string current = "";
		fullText = true;
		var tokens = TokenizeForWrap(text)
			.SelectMany(token => token != "\n" && MeasureTextWidth(graphics, token, font) > availableWidth
				? token.Select(ch => ch.ToString())
				: new[] { token });
		foreach (var token in tokens)
		{
			if (token == "\n")
			{
				if (current.Trim().Length > 0) lines.Add(current.Trim());
				current = "";
				if (lines.Count >= maxLines)
				{
					fullText = false;
					break;
				}
				continue;
			}
			string candidate = current + token;
			if (current.Length > 0 && MeasureTextWidth(graphics, candidate, font) > availableWidth)
			{
				lines.Add(current.Trim());
				current = string.IsNullOrWhiteSpace(token) ? "" : token;
				if (lines.Count == maxLines)
				{
					fullText = false;
					break;
				}
			}
			else
			{
				current = candidate;
			}
		}
		if (fullText && current.Length > 0)
		{
			lines.Add(current.Trim());
		}
		lines = lines.Where(line => line.Length > 0).ToList();
		if (lines.Count == 0)
		{
			lines.Add(text.Length > 0 ? text.Substring(0, 1) : "");
		}
		if (lines.Count > maxLines)
		{
			fullText = false;
			lines = lines.Take(maxLines).ToList();
		}
		if (trimWithEllipsis && !fullText)
		{
			int last = lines.Count - 1;
			lines[last] = FitWithEllipsis(graphics, lines[last], font, availableWidth);
		}
		return lines.ToArray();
	}

	// --- TokenizeForWrap (CAD) ---
	private static IEnumerable<string> TokenizeForWrap(string text)
	{
		for (var index = 0; index < (text ?? "").Length; index++)
		{
			var ch = text[index];
			if (ch == '\r') continue;
			if (ch == '\n')
			{
				yield return "\n";
				continue;
			}
			if (ch <= 127 && !char.IsWhiteSpace(ch))
			{
				var start = index;
				while (index + 1 < text.Length && text[index + 1] <= 127 && !char.IsWhiteSpace(text[index + 1])) index++;
				yield return text.Substring(start, index - start + 1);
				continue;
			}
			yield return ch.ToString();
		}
	}

	// --- BalanceLines (CAD) ---
	private static string[] BalanceLines(Graphics graphics, string text, string[] fallback, Font font, float availableWidth)
	{
		if (fallback.Length <= 1 || text.IndexOfAny(new[] { '\r', '\n' }) >= 0
			|| text.Any(ch => ch <= 127 && !char.IsWhiteSpace(ch))) return fallback;
		var value = text.Trim();
		if (value.Length < fallback.Length) return fallback;
		var balanced = new List<string>(fallback.Length);
		var offset = 0;
		for (var line = 0; line < fallback.Length; line++)
		{
			var remainingCharacters = value.Length - offset;
			var remainingLines = fallback.Length - line;
			var count = (int)Math.Ceiling(remainingCharacters / (double)remainingLines);
			var part = value.Substring(offset, Math.Min(count, remainingCharacters)).Trim();
			if (part.Length == 0 || MeasureTextWidth(graphics, part, font) > availableWidth) return fallback;
			balanced.Add(part);
			offset += count;
		}
		return offset >= value.Length ? balanced.ToArray() : fallback;
	}

	// --- FitWithEllipsis (CAD) ---
	private static string FitWithEllipsis(Graphics graphics, string text, Font font, float availableWidth)
	{
		const string ellipsis = "…";
		string value = text ?? "";
		while (value.Length > 0 && MeasureTextWidth(graphics, value + ellipsis, font) > availableWidth)
		{
			value = value.Substring(0, value.Length - 1);
		}
		return value.Length == 0 ? ellipsis : value + ellipsis;
	}

	// --- MeasureLines (CAD: TextRenderer-based) ---
	private static SizeF MeasureLines(Graphics graphics, string[] lines, Font font)
	{
		float width = 0f;
		foreach (string line in lines)
		{
			width = Math.Max(width, MeasureTextWidth(graphics, line, font));
		}
		return new SizeF(width, MeasureTextHeight(graphics, font) * lines.Length);
	}

	// --- GetIcon (CAD) ---
	private Image? GetIcon(string? path)
	{
		path = (path ?? "").Trim();
		if (path.Length == 0 || !File.Exists(path)) return null;
		if (_iconCache.TryGetValue(path, out var cached)) return cached;
		try
		{
			using var source = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
			var image = new Bitmap(source);
			_iconCache[path] = image;
			return image;
		}
		catch (Exception ex)
		{
			Logger.Error("命令图标读取失败：" + path, ex);
			return null;
		}
	}

	// --- DrawIcon (CAD) ---
	private static void DrawIcon(Graphics graphics, Image icon, PointF center, float size)
	{
		var scale = Math.Min(size / Math.Max(1f, icon.Width), size / Math.Max(1f, icon.Height));
		var width = Math.Max(1f, icon.Width * scale);
		var height = Math.Max(1f, icon.Height * scale);
		WheelIconDrawing.Draw(graphics, icon, new RectangleF(center.X - width / 2f, center.Y - height / 2f, width, height));
	}

	// --- Shared helpers ---
	private static RectangleF Offset(RectangleF rect, float dx, float dy)
	{
		return new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
	}

	private static Rectangle Offset(Rectangle rect, int dx, int dy)
	{
		return new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
	}

	private static bool IsLightColor(Color color)
	{
		return color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 185;
	}

	private static bool HasTextColor(string value)
	{
		return !string.IsNullOrWhiteSpace(value);
	}

	private Color ParseTextColor(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return WheelTextColor;
		}
		try
		{
			return ColorTranslator.FromHtml(text);
		}
		catch
		{
			return WheelTextColor;
		}
	}
}



