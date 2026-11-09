using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal enum SettingsWindowMode
{
	Wheel,
	FeatureTools
}

internal sealed partial class SettingsWindow : PluginToolWindow
{
	private sealed class SceneChoice
	{
		public string Key { get; }

		public SceneChoice(string key)
		{
			Key = key;
		}

		public override string ToString()
		{
			return SceneKeys.Display(Key);
		}
	}

	private sealed class TriggerChoice
	{
		public string Key { get; }

		public TriggerChoice(string key)
		{
			Key = key;
		}

		public override string ToString()
		{
			return TriggerModes.Display(Key);
		}
	}

	private readonly struct RingDefinition
	{
		public int Index { get; }

		public int Slots { get; }

		public int SizeWeight { get; }

		public int DiameterPx { get; }

		public RingDefinition(int index, int slots, int sizeWeight, int diameterPx)
		{
			Index = index;
			Slots = slots;
			SizeWeight = sizeWeight;
			DiameterPx = diameterPx;
		}
	}

	private readonly struct CommandRowData
	{
		public int Ring { get; }

		public int Slot { get; }

		public string Label { get; }

		public string Kind { get; }

		public string Value { get; }

		public int FontSizePx { get; }

		public string FontName { get; }

		public string TextColor { get; }

		public List<ComboCommandConfig> ComboCommands { get; }

		public bool ComboAdvanceEnabled { get; }

		public string ComboAdvanceKey { get; }

		public bool? CellBold { get; }
		public string CellEffect { get; }
		public string IconId { get; }
		public string IconPath { get; }
		public string ContentMode { get; }

		// Carries the per-cell geometry across a grid rebuild.
		public ComboRowState CellState { get; }

		public CommandRowData(int ring, int slot, string label, string kind, string value, int fontSizePx, string fontName, string textColor, List<ComboCommandConfig>? comboCommands = null, bool comboAdvanceEnabled = false, string comboAdvanceKey = "Ctrl", string iconId = "", string iconPath = "", string contentMode = "", bool? cellBold = null, string cellEffect = "", ComboRowState cellState = null)
		{
			Ring = ring;
			Slot = slot;
			Label = label;
			Kind = kind;
			Value = value;
			FontSizePx = fontSizePx;
			FontName = fontName;
			TextColor = textColor;
			ComboCommands = comboCommands ?? new List<ComboCommandConfig>();
			ComboAdvanceEnabled = comboAdvanceEnabled;
			ComboAdvanceKey = ComboAdvanceKeys.Normalize(comboAdvanceKey);
			IconId = iconId; IconPath = iconPath; ContentMode = contentMode; CellBold = cellBold; CellEffect = cellEffect;
			CellState = cellState;
		}
	}

	private sealed class SettingChoice
	{
		public string Key { get; }

		private string DisplayName { get; }

		public SettingChoice(string key, string displayName)
		{
			Key = key;
			DisplayName = displayName;
		}

		public override string ToString()
		{
			return DisplayName;
		}
	}

	private sealed class ComboRowState
	{
		public bool? CellBold { get; set; }
		public string CellEffect { get; set; } = "";
		public string IconId { get; set; } = "";
		public string IconPath { get; set; } = "";
		public string ContentMode { get; set; } = "";
		public string IconData { get; set; } = "";
		public string ImageMode { get; set; } = "Icon";
		public List<ComboCommandConfig> Commands { get; set; } = new List<ComboCommandConfig>();

		public bool AdvanceEnabled { get; set; }

		public string AdvanceKey { get; set; } = ComboAdvanceKeys.Default;

		// Per-cell sizing carried through the grid so editing one cell never
		// resets the geometry configured on another.
		public int CellSizePercent { get; set; } = 100;
		public string CellShape { get; set; } = "Circle";
		public int IconRatioPercent { get; set; } = 55;
		public double? FreeX { get; set; }
		public double? FreeY { get; set; }
		public bool StopOnStepError { get; set; } = true;

		public ComboRowState WithGeometryFrom(MenuItemConfig item)
		{
			CopyFrom(item);
			return this;
		}

		public void CopyFrom(MenuItemConfig item)
		{
			if (item == null) return;
			CellSizePercent = item.CellSizePercent;
			CellShape = item.CellShape;
			IconRatioPercent = item.IconRatioPercent;
			FreeX = item.FreeX;
			FreeY = item.FreeY;
			StopOnStepError = item.StopOnStepError;
			IconData = item.IconData;
			ImageMode = item.ImageMode;
		}

		public void CopyFrom(ComboRowState other)
		{
			if (other == null) return;
			CellSizePercent = other.CellSizePercent;
			CellShape = other.CellShape;
			IconRatioPercent = other.IconRatioPercent;
			FreeX = other.FreeX;
			FreeY = other.FreeY;
			StopOnStepError = other.StopOnStepError;
			IconData = other.IconData;
			ImageMode = other.ImageMode;
		}

		public void CopyTo(MenuItemConfig item)
		{
			if (item == null) return;
			item.CellSizePercent = CellSizePercent;
			item.CellShape = CellShape;
			item.IconRatioPercent = IconRatioPercent;
			item.FreeX = FreeX;
			item.FreeY = FreeY;
			item.StopOnStepError = StopOnStepError;
			item.IconData = IconData;
			item.ImageMode = ImageMode;
		}
	}

	private readonly Func<PluginConfig> _load;

	private readonly Action<PluginConfig> _save;

	private readonly Func<IntPtr> _getSolidWorksWindowHandle;

	private readonly Func<ISldWorks> _getSolidWorks;

	private readonly Func<ICommandManager> _getCommandManager;

	private readonly SettingsWindowMode _mode;

	private readonly ComboBox _scene = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 160
	};

	private readonly DataGridView _grid = new DataGridView
	{
		Dock = DockStyle.Fill,
		AllowUserToAddRows = false,
		RowHeadersVisible = false
	};

	private readonly ComboBox _triggerMode = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 180
	};

	private readonly NumericUpDown _hold = new NumericUpDown
	{
		Minimum = 150m,
		Maximum = 2000m,
		Increment = 50m,
		Width = 80
	};

	private readonly TrackBar _menuOpacity = new TrackBar
	{
		Minimum = 20,
		Maximum = 100,
		TickFrequency = 10,
		SmallChange = 5,
		LargeChange = 10,
		Width = 180
	};

	private readonly Label _menuOpacityValue = new Label
	{
		AutoSize = true,
		Padding = new Padding(8, 6, 0, 0)
	};

	private readonly Label _selectedCellLabel = new Label
	{
		AutoSize = true,
		Padding = new Padding(12, 7, 4, 0),
		ForeColor = Color.FromArgb(64, 78, 92)
	};

	private readonly TrackBar _backgroundOpacity = new TrackBar
	{
		Minimum = 0,
		Maximum = 100,
		TickFrequency = 10,
		SmallChange = 5,
		LargeChange = 10,
		Width = 180
	};

	private readonly Label _backgroundOpacityValue = new Label
	{
		AutoSize = true,
		Padding = new Padding(8, 6, 0, 0)
	};

	private readonly NumericUpDown _menuSize = new NumericUpDown
	{
		Minimum = 260m,
		Maximum = 900m,
		Increment = 10m,
		Width = 80
	};

	private readonly NumericUpDown _centerRadius = new NumericUpDown
	{
		Minimum = 20m,
		Maximum = 250m,
		Increment = 2m,
		Width = 80
	};

	private readonly Dictionary<string, CheckBox> _sceneSwitches = new Dictionary<string, CheckBox>();

	private readonly CheckBox _globalComboAdvance = new CheckBox
	{
		Text = "全局组合衔接",
		AutoSize = true,
		Margin = new Padding(4, 9, 10, 0)
	};

	private readonly ComboBox _comboAdvanceKey = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 92,
		Margin = new Padding(4, 5, 10, 0)
	};

	private readonly NumericUpDown _fontSize = new NumericUpDown
	{
		Minimum = 6m,
		Maximum = 72m,
		Increment = 1m,
		Width = 80
	};

	private readonly CheckBox _autoFitCellFont = new CheckBox
	{
		Text = "自动适配格子",
		AutoSize = true,
		Margin = new Padding(8, 7, 0, 0)
	};

	private readonly ComboBox _fontName = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 160
	};

	private readonly Button _cellTextColor = new Button
	{
		Text = "选择颜色",
		Width = 92,
		Height = 28,
		FlatStyle = FlatStyle.Flat,
		BackColor = Color.FromArgb(18, 28, 38),
		ForeColor = Color.White,
		Margin = new Padding(0, 2, 8, 2)
	};

	private readonly NumericUpDown _ringCount = new NumericUpDown
	{
		Minimum = 1m,
		Maximum = 4m,
		Increment = 1m,
		Width = 64
	};

	private readonly FlowLayoutPanel _ringEditor = new FlowLayoutPanel
	{
		Dock = DockStyle.Top,
		AutoSize = true,
		WrapContents = false,
		FlowDirection = FlowDirection.TopDown,
		Padding = new Padding(0, 4, 0, 8)
	};

	// Equal band widths are what users expect: every ring the same thickness,
	// no matter how many rings are configured. Unchecking hands the diameters
	// back to the per-ring inputs below.
	private readonly CheckBox _equalRingBands = new CheckBox
	{
		Text = "各圈等宽（每圈径向宽度相同）",
		Dock = DockStyle.Top,
		AutoSize = true,
		Checked = true,
		Padding = new Padding(0, 6, 0, 2)
	};

	private readonly List<NumericUpDown> _ringSlotInputs = new List<NumericUpDown>();

	private readonly List<NumericUpDown> _ringDiameterInputs = new List<NumericUpDown>();

	private readonly Timer _ringSyncTimer = new Timer
	{
		Interval = 260
	};

	private readonly TextBox _backgroundPath = new TextBox
	{
		Width = 260
	};

	private readonly TextBox _shortcutSearch = new TextBox
	{
		Width = 260
	};

	private readonly ComboBox _processDefaultMode = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 180
	};

	private readonly CheckBox _processEnabled = new CheckBox { Text = "启用工序识别", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _propertyWatermarkEnabled = new CheckBox { Text = "启用属性水印", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _radialMenuEnabled = new CheckBox { Text = "启用轮盘与鼠标触发", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _shortcutPaletteEnabled = new CheckBox { Text = "启用快捷键候选", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _explodeEnabled = new CheckBox { Text = "启用一键爆炸", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly TextBox _processSummaryProperty = new TextBox { Width = 220 };

	private readonly TextBox _processSeparator = new TextBox { Width = 80 };

	private readonly ComboBox _processSurfaceMode = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 220
	};

	private readonly TextBox _processSurfaceProperty = new TextBox { Width = 220 };

	private readonly TextBox _processSurfaceFallback = new TextBox { Width = 220 };

	private readonly TextBox _processSurfaceRules = new TextBox { Dock = DockStyle.Fill };

	private readonly TextBox _processCraftProperty = new TextBox { Width = 220 };

	private readonly TextBox _processThicknessProperty = new TextBox { Width = 220 };

	private readonly TextBox _processBendCountProperty = new TextBox { Width = 220 };

	private readonly TextBox _processCutoutCountProperty = new TextBox { Width = 220 };

	private readonly TextBox _processPartTypeProperty = new TextBox { Width = 220 };

	private readonly TextBox _processProcessTypeProperty = new TextBox { Width = 220 };

	private readonly TextBox _processRemarkProperty = new TextBox { Width = 220 };

	private readonly TextBox _processOutsourceProperty = new TextBox { Width = 220 };

	private readonly TextBox _processDeburrName = new TextBox { Width = 220 };

	private readonly TextBox _processInspectionName = new TextBox { Width = 220 };

	private readonly TextBox _processPackagingName = new TextBox { Width = 220 };

	private readonly TextBox _processShippingName = new TextBox { Width = 220 };

	private readonly TextBox _processAdditionalProcesses = new TextBox { Dock = DockStyle.Fill };

	private readonly TextBox _processNameMappings = new TextBox { Dock = DockStyle.Fill };
	private string _processTypeGroups = "";
	private readonly TextBox _processCombinations = new TextBox { Dock = DockStyle.Fill };
	private readonly TextBox _processCategories = new TextBox { Dock = DockStyle.Fill };

	private readonly NumericUpDown _processAssemblyBatchSize = new NumericUpDown { Minimum = 1, Maximum = 10, Width = 80 };

	private readonly NumericUpDown _processAssemblyBatchDelay = new NumericUpDown { Minimum = 800, Maximum = 5000, Increment = 100, Width = 90 };

	private readonly CheckBox _processWatermark = new CheckBox { Text = "写入后刷新属性水印", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processCraftDescription = new CheckBox { Text = "写入工艺说明", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processGeometryProperties = new CheckBox { Text = "写入板厚/折弯下刀数/内轮廓数", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processAssemblyAuto = new CheckBox { Text = "开启工序识别后自动后台识别", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processDeburr = new CheckBox { Text = "自动补去毛刺", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processInspection = new CheckBox { Text = "自动补检验", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processInspectionAfterSurface = new CheckBox { Text = "检验放在表处后", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processPackaging = new CheckBox { Text = "自动补包装", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	private readonly CheckBox _processShipping = new CheckBox { Text = "自动补出货", AutoSize = true, Margin = new Padding(0, 8, 16, 0) };

	// Recognition preference: a part file used by one instance is a safer guess
	// for "this is the panel" than one of several identical copies.

	private readonly DataGridView _shortcutGrid = new DataGridView
	{
		Dock = DockStyle.Fill,
		ReadOnly = true,
		AllowUserToAddRows = false,
		RowHeadersVisible = false,
		SelectionMode = DataGridViewSelectionMode.FullRowSelect,
		MultiSelect = false,
		BackgroundColor = Color.White,
		BorderStyle = BorderStyle.None
	};

	private readonly Panel _content = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = Color.FromArgb(248, 250, 252),
		Padding = new Padding(18)
	};

	private readonly TreeView _commandCategoryTree = new TreeView
	{
		Dock = DockStyle.Fill,
		BorderStyle = BorderStyle.None,
		HideSelection = false,
		ShowLines = true,
		ShowPlusMinus = true,
		ShowRootLines = true,
		BackColor = Color.White
	};

	private TableLayoutPanel? _workspace;

	private Panel? _editorPane;

	private Control? _sceneBar;

	private Control? _previewPane;

	private readonly RadialMenuControl _preview = new RadialMenuControl(Color.White)
	{
		Dock = DockStyle.Fill,
		ScaleMenuForPreview = true
	};

	private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

	private List<KeyboardShortcutRow> _shortcutRows = new List<KeyboardShortcutRow>();

	private PluginConfig _config;
	private ProcessRecognitionConfig _processEditorBaseline;

	private string _activeSceneKey = "";

	private string _activeSection = "basic";

	private int? _selectedCommandRing;

	private int? _selectedCommandSlot;

	private bool _loadingScene;

	private bool _syncingRingInputs;

	private bool _refreshingPreview;

	private bool _syncingCellAppearance;

	public SettingsWindow(Func<PluginConfig> load, Action<PluginConfig> save, Func<IntPtr>? getSolidWorksWindowHandle = null, Func<ISldWorks>? getSolidWorks = null, Func<ICommandManager>? getCommandManager = null, SettingsWindowMode mode = SettingsWindowMode.Wheel)
	{
		_mode = mode;
		PreserveAppearance(_cellTextColor);
		_load = load;
		_save = save;
		_getSolidWorksWindowHandle = getSolidWorksWindowHandle ?? (() => IntPtr.Zero);
		_getSolidWorks = getSolidWorks ?? (() => null);
		_getCommandManager = getCommandManager ?? (() => null);
		_config = _load();
		_processDefaultMode.Items.Add(new SettingChoice(ProcessRecognitionModes.Deep, "深度识别（默认）"));
		_processDefaultMode.Items.Add(new SettingChoice(ProcessRecognitionModes.Quick, "快速识别"));
		_processSurfaceMode.Items.Add(new SettingChoice(SurfaceRecognitionModes.Material, "按材质映射，属性值优先"));
		_processSurfaceMode.Items.Add(new SettingChoice(SurfaceRecognitionModes.Property, "只读表面处理属性"));
		_processSurfaceMode.Items.Add(new SettingChoice(SurfaceRecognitionModes.Fixed, "固定使用回退工序"));
		_processEnabled.CheckedChanged += (_, __) => UpdateProcessRecognitionControlState();
		_processInspection.CheckedChanged += (_, __) => UpdateFeatureSwitchDependencies();
		_propertyWatermarkEnabled.CheckedChanged += (_, __) => UpdateFeatureSwitchDependencies();
		_radialMenuEnabled.CheckedChanged += (_, __) => UpdateFeatureSwitchDependencies();
		_globalComboAdvance.CheckedChanged += (_, __) => UpdateFeatureSwitchDependencies();
		LoadProcessRecognitionControls();
		foreach (string key in ComboAdvanceKeys.Choices)
		{
			_comboAdvanceKey.Items.Add(key);
		}
		LoadInstalledFontNames();
		SelectGlobalAdvanceKey(_config.ComboAdvanceKey);
		_globalComboAdvance.Checked = _config.GlobalComboAdvanceEnabled;
		Text = mode == SettingsWindowMode.Wheel ? "XY轮盘设置" : "XY轮盘功能设置";
		base.Width = 1280;
		base.Height = 760;
		MinimumSize = new Size(1040, 640);
		base.StartPosition = FormStartPosition.CenterScreen;
		base.KeyPreview = true;
		BackColor = Color.FromArgb(248, 250, 252);
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
		DoubleBuffered = true;
		_ringSyncTimer.Tick += delegate
		{
			_ringSyncTimer.Stop();
			SyncCommandRowsToRingDefinitions();
			RefreshPreview();
			if (!string.IsNullOrWhiteSpace(_activeSceneKey))
			{
				SetRingInputsFromScene(GetScene(_activeSceneKey));
			}
		};
		string[] all = SceneKeys.All;
		foreach (string key in all)
		{
			_scene.Items.Add(new SceneChoice(key));
		}
		foreach (KeyValuePair<string, string> item in TriggerModes.All)
		{
			_triggerMode.Items.Add(new TriggerChoice(item.Key));
		}
		_scene.SelectedIndexChanged += delegate
		{
			ChangeScene();
		};
		BuildGrid();
		_grid.CellValueChanged += delegate
		{
			RefreshPreview();
		};
		_grid.CellEndEdit += delegate
		{
			RefreshPreview();
		};
		_grid.RowsAdded += delegate
		{
			RefreshPreview();
		};
		_grid.RowsRemoved += delegate
		{
			RefreshPreview();
		};
		_grid.CurrentCellDirtyStateChanged += delegate
		{
			if (_grid.IsCurrentCellDirty)
			{
				_grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
		};
		base.Controls.Add(BuildShell());
		StyleInteractiveControls(this);
		ClientSizeChanged += delegate
		{
			UpdateWorkspaceLayout();
		};
		SelectTriggerMode(_config.TriggerMode);
		_hold.Value = _config.HoldDelayMs;
		_menuOpacity.Value = Math.Max(_menuOpacity.Minimum, Math.Min(_menuOpacity.Maximum, _config.OpacityPercent));
		_menuSize.Value = Math.Max(_menuSize.Minimum, Math.Min(_menuSize.Maximum, _config.MenuSizePx));
		_centerRadius.Value = Math.Max(_centerRadius.Minimum, Math.Min(_centerRadius.Maximum, _config.CenterRadiusPx));
		_triggerMode.SelectedIndexChanged += delegate
		{
			RefreshPreview();
		};
		_hold.ValueChanged += delegate
		{
			RefreshPreview();
		};
		_menuOpacity.ValueChanged += delegate
		{
			_menuOpacityValue.Text = _menuOpacity.Value + "%";
			RefreshPreview();
		};
		_backgroundOpacity.ValueChanged += delegate
		{
			_backgroundOpacityValue.Text = _backgroundOpacity.Value + "%";
			RefreshPreview();
		};
		_menuSize.ValueChanged += delegate
		{
			if (!_loadingScene && !_syncingRingInputs)
			{
				RebuildRingEditor();
				QueueRingDefinitionSync();
			}
			RefreshPreview();
		};
		_centerRadius.ValueChanged += delegate
		{
			if (!_loadingScene && !_syncingRingInputs)
			{
				RebuildRingEditor();
				QueueRingDefinitionSync();
			}
			RefreshPreview();
		};
		_fontSize.ValueChanged += delegate
		{
			ApplySelectedCellFontSize();
		};
		_autoFitCellFont.CheckedChanged += delegate
		{
			ApplySelectedCellFontSize();
		};
		_fontName.SelectedIndexChanged += delegate
		{
			ApplySelectedCellFontName();
		};
		_cellTextColor.Click += delegate
		{
			PickSelectedCellTextColor();
		};
		_backgroundPath.TextChanged += delegate
		{
			RefreshPreview();
		};
		_ringCount.ValueChanged += delegate
		{
			RingCountChanged();
		};
		_equalRingBands.CheckedChanged += delegate
		{
			if (!_syncingRingInputs)
			{
				RebuildRingEditor();
			}
		};
		_shortcutSearch.TextChanged += delegate
		{
			RefreshShortcutRows();
		};
		_shortcutSearch.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Enter) return;
			e.SuppressKeyPress = true;
			ResolveTypedFunctionForCurrentCell();
		};
		_commandCategoryTree.AfterSelect += delegate
		{
			RefreshShortcutRows();
		};
		BuildShortcutGrid();
		_preview.EnableCellSelection = true;
		_preview.CellsSwapped += SwapWheelCells;
		_preview.CellSelected += delegate(object _, RadialCellSelectedEventArgs cell)
		{
			SelectCommandCell(cell.RingIndex + 1, cell.Slot + 1);
		};
		_loadingScene = true;
		_radialMenuEnabled.Checked = _config.RadialMenuEnabled;
		_shortcutPaletteEnabled.Checked = _config.ShortcutPaletteEnabled;
		_explodeEnabled.Checked = _config.ExplodeEnabled;
		LoadUserMacroSettings();
		_propertyWatermarkEnabled.Checked = _config.PropertyWatermark?.Enabled == true;
		UpdateFeatureSwitchDependencies();
		_loadingScene = false;
		SelectScene(ResolveCurrentSolidWorksScene());
		if (_scene.SelectedIndex < 0)
		{
			_scene.SelectedIndex = 0;
		}
		RenderSection(mode == SettingsWindowMode.Wheel ? "basic" : "features");
		InitializeResponsiveSettingsLayout();
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if ((keyData & Keys.KeyCode) == Keys.Escape)
		{
			Close();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_ringSyncTimer.Dispose();
			CleanupPendingUserMacroImports();
		}
		base.Dispose(disposing);
	}

	private Control BuildShell()
	{
		return new Panel
		{
			Dock = DockStyle.Fill,
			Controls = 
			{
				BuildWorkspace(),
				BuildSidebar()
			}
		};
	}

	private Control BuildSidebar()
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Left,
			Width = 196,
			BackColor = Color.FromArgb(30, 43, 39)
		};
		PreserveAppearance(panel);
		_globalComboAdvance.CheckedChanged += delegate
		{
			if (!_loadingScene)
			{
				ApplyGlobalComboAdvanceToRowsAndConfig();
			}
		};
		_comboAdvanceKey.SelectedIndexChanged += delegate
		{
			if (!_loadingScene && _globalComboAdvance.Checked)
			{
				ApplyGlobalComboAdvanceToRowsAndConfig();
			}
		};
		_comboAdvanceKey.KeyDown += delegate(object _, KeyEventArgs e)
		{
			SelectGlobalAdvanceKey(ComboAdvanceKeys.FromKeyCode(e.KeyCode));
			e.SuppressKeyPress = true;
		};
		Label value = new Label
		{
			Text = "XY轮盘",
			ForeColor = Color.White,
			Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
			Location = new Point(14, 12),
			Size = new Size(168, 26),
			TextAlign = ContentAlignment.MiddleLeft
		};
		Label value2 = new Label
		{
			Text = "轮盘 · 水印 · 工序 · 爆炸",
			ForeColor = Color.FromArgb(210, 218, 226),
			Location = new Point(14, 43),
			Size = new Size(168, 18)
		};
		panel.Controls.Add(value);
		panel.Controls.Add(value2);
		int num = 82;
		(string, string)[] array = _mode == SettingsWindowMode.Wheel
			? new[] { ("basic", "基础"), ("features", "功能开关"), ("commands", "格子命令"), ("hotkeys", "命令库"), ("appearance", "外观"), ("fonts", "字体与适配"), ("preview", "查看轮盘") }
			: new[] { ("features", "功能开关"), ("process", "工序识别"), ("about", "关于"), ("help", "功能说明") };
		for (int i = 0; i < array.Length; i++)
		{
			(string, string) tuple = array[i];
			Button button = new Button
			{
				Text = tuple.Item2,
				FlatStyle = FlatStyle.Flat,
				TextAlign = ContentAlignment.MiddleLeft,
				ForeColor = Color.White,
				BackColor = Color.FromArgb(30, 43, 39),
				Location = new Point(0, num),
				Size = new Size(panel.Width, 34),
				Padding = new Padding(20, 0, 0, 0),
				TabStop = false
			};
			button.FlatAppearance.BorderSize = 0;
			var (section, _) = tuple;
			button.Click += delegate
			{
				RenderSection(section);
			};
			_navButtons[section] = button;
			panel.Controls.Add(button);
			num += 40;
		}
		return panel;
	}

	private Control BuildWorkspace()
	{
		_workspace = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(248, 250, 252),
			ColumnCount = 2,
			RowCount = 1,
			ColumnStyles =
			{
				new ColumnStyle(SizeType.Percent, 58f),
				new ColumnStyle(SizeType.Percent, 42f)
			}
		};
		_editorPane = (Panel)BuildEditor();
		_previewPane = BuildPreviewPane();
		_workspace.Controls.Add(_editorPane, 0, 0);
		_workspace.Controls.Add(_previewPane, 1, 0);
		return _workspace;
	}

	private Control BuildEditor()
	{
		_sceneBar = BuildSceneBar();
		return new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(248, 250, 252),
			Controls = 
			{
				(Control)_content,
				_sceneBar,
				BuildActionBar()
			}
		};
	}

	private Control BuildSceneBar()
	{
		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Height = 118,
			Padding = new Padding(18, 9, 18, 5),
			BackColor = Color.FromArgb(248, 250, 252),
			ColumnCount = 1,
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

		FlowLayoutPanel sceneRow = ToolbarRow();
		sceneRow.Controls.Add(new Label
		{
			Text = "当前轮盘/页面",
			AutoSize = true,
			Padding = new Padding(0, 7, 8, 0)
		});
		sceneRow.Controls.Add(_scene);

		FlowLayoutPanel quickRow = ToolbarRow();
		quickRow.AutoSize = true;
		quickRow.WrapContents = true;
		string[] all = SceneKeys.All;
		foreach (string text in all)
		{
			string sceneKey = text;
			var page = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
				WrapContents = false, Margin = Padding.Empty, BackColor = quickRow.BackColor };
			var enabled = new CheckBox { AutoSize = true, Margin = new Padding(0, 8, 4, 0),
				Name = "scene-enabled-" + sceneKey, AccessibleName = "启用" + SceneKeys.Display(sceneKey) };
			_sceneSwitches.Add(sceneKey, enabled);
			enabled.CheckedChanged += (_, __) =>
			{
				if (_loadingScene) return;
				GetScene(sceneKey).Enabled = enabled.Checked;
				if (sceneKey == SceneKeys.Sketch)
				{
					_config.SketchWheelEnabled = enabled.Checked;
				}
			};
			page.Controls.Add(enabled);
			page.Controls.Add(MakeButton(SceneKeys.Display(text), delegate
			{
				SelectScene(sceneKey);
			}, 82));
			quickRow.Controls.Add(page);
		}
		ToolTip sketchTip = new ToolTip();
		foreach (var entry in _sceneSwitches)
			sketchTip.SetToolTip(entry.Value, "启用" + SceneKeys.Display(entry.Key) + "轮盘；关闭后该场景保留原生鼠标操作。保存后生效。");

		FlowLayoutPanel clearRow = ToolbarRow();
		clearRow.Controls.Add(MakeButton("清空当前轮盘", ClearCurrentWheel, 122));

		layout.Controls.Add(sceneRow, 0, 0);
		layout.Controls.Add(quickRow, 0, 1);
		layout.Controls.Add(clearRow, 0, 2);
		return layout;
	}

	private Control BuildActionBar()
	{
		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 52,
			Padding = new Padding(18, 8, 18, 8),
			BackColor = Color.FromArgb(248, 250, 252),
			ColumnCount = 2,
			RowCount = 1
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		FlowLayoutPanel backupActions = ToolbarRow();
		backupActions.Controls.Add(MakeButton("导出配置备份", ExportConfigBackup, 118));
		backupActions.Controls.Add(MakeButton("导入配置备份", ImportConfigBackup, 118));
		FlowLayoutPanel saveActions = ToolbarRow(FlowDirection.RightToLeft);
		saveActions.AutoSize = true;
		saveActions.Controls.Add(MakeButton("关闭", base.Close, 82));
		saveActions.Controls.Add(MakeButton(_mode == SettingsWindowMode.Wheel ? "应用并保存" : "应用功能设置", SaveConfigFromUi, 118, primary: true));
		if (_mode == SettingsWindowMode.Wheel)
		{
			layout.Controls.Add(backupActions, 0, 0);
		}
		int saveActionsColumn = _mode == SettingsWindowMode.Wheel ? 1 : 0;
		layout.Controls.Add(saveActions, saveActionsColumn, 0);
		if (_mode == SettingsWindowMode.FeatureTools) layout.SetColumnSpan(saveActions, 2);
		return layout;
	}

	private Control BuildPreviewPane()
	{
		Panel obj = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(232, 241, 236),
			Padding = new Padding(24)
		};
		Label value = new Label
		{
			Text = "预览",
			Dock = DockStyle.Top,
			Height = 32,
			ForeColor = Color.FromArgb(46, 62, 74),
			Font = new Font(Font, FontStyle.Bold)
		};
		// Cell-editing tools live above the wheel; the appearance toolbar is no
		// longer here, so the preview is not crowded by buttons unrelated to it.
		Control tools = BuildPreviewCellTools();
		Control hint = MakePreviewHint();
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			Padding = new Padding(10)
		};
		panel.Controls.Add(_preview);
		obj.Controls.Add(panel);
		obj.Controls.Add(hint);
		obj.Controls.Add(tools);
		obj.Controls.Add(value);
		return obj;
	}

	private void RenderSection(string section)
	{
		EndPreviewEditing();
		_activeSection = section;
		UpdateWorkspaceLayout();
		foreach (KeyValuePair<string, Button> navButton in _navButtons)
		{
			navButton.Value.BackColor = ((navButton.Key == section) ? Accent : Color.FromArgb(30, 43, 39));
		}
		_content.Controls.Clear();
		Control.ControlCollection controls = _content.Controls;
		controls.Add(section switch
		{
			"features" => BuildFeatureSwitchSection(),
			"process" => BuildProcessRecognitionSection(),
			"fonts" => BuildFontSection(),
			"commands" => BuildCommandSection(), 
			"hotkeys" => BuildHotkeySection(), 
			"appearance" => BuildAppearanceSection(), 
			"about" => BuildAboutPage(), 
			"help" => BuildHelpPage(), 
			_ => BuildBasicSection(), 
		});
	}

	private void UpdateWorkspaceLayout()
	{
		bool contentOnly = _activeSection.Equals("process", StringComparison.OrdinalIgnoreCase)
			|| _activeSection.Equals("features", StringComparison.OrdinalIgnoreCase)
			|| _activeSection.Equals("about", StringComparison.OrdinalIgnoreCase)
			|| _activeSection.Equals("help", StringComparison.OrdinalIgnoreCase);
		bool previewOnly = _activeSection == "preview";
		bool wideEditor = !previewOnly && (contentOnly || ClientSize.Width < 1100);
		if (_workspace != null && _editorPane != null && _previewPane != null)
		{
			_previewPane.Visible = !wideEditor;
			_editorPane.Visible = !previewOnly;
			_workspace.SetColumnSpan(_editorPane, wideEditor ? 2 : 1);
			if (previewOnly)
			{
				_workspace.ColumnStyles[0].Width = 0;
				_workspace.ColumnStyles[1].Width = 100;
			}
			else if (!wideEditor)
			{
				// The preview column was always narrower than the wheel needed.
				// Re-balance so the wheel reads at a glance: the editor gets
				// roughly half the workspace on commands and appearance, and a
				// still-legible third on hotkeys where there are many rows.
				float editorWidth = _activeSection.Equals("hotkeys", StringComparison.OrdinalIgnoreCase) ? 60f
					: _activeSection.Equals("commands", StringComparison.OrdinalIgnoreCase) ? 52f : 50f;
				_workspace.ColumnStyles[0].Width = editorWidth;
				_workspace.ColumnStyles[1].Width = 100f - editorWidth;
			}
		}
		if (_sceneBar != null)
		{
			_sceneBar.Visible = !contentOnly;
		}
	}

	private Control BuildBasicSection()
	{
		Panel panel = Page();
		panel.Controls.Add(Fields(Field("触发方式", _triggerMode), Field("长按时间 ms", _hold), Field("菜单透明 %", BuildMenuOpacityControl())));
		var wheelOptions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 10) };
		wheelOptions.Controls.AddRange(new Control[] { _radialMenuEnabled, _globalComboAdvance, _comboAdvanceKey });
		panel.Controls.Add(wheelOptions);
		panel.Controls.Add(SectionTitle("基础"));
		return panel;
	}

	private Control BuildFeatureSwitchSection()
	{
		Panel panel = Page();
		var groups = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 8) };
		groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		groups.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		groups.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		groups.Controls.Add(FeatureGroup("轮盘", "按场景配置格子，支持原生命令、外部命令及组合录制。", _radialMenuEnabled, _shortcutPaletteEnabled,
			_commandTabEnabled, Fields(Field("选项卡名称", _commandTabName))), 0, 0);
		groups.Controls.Add(FeatureGroup("属性水印", "显示当前文档或选中零件属性；在“属性水印设置”中编辑字段与样式。", _propertyWatermarkEnabled), 1, 0);
		var process = FeatureGroup("工序", "开启后自动后台识别；工序规则、名称和分组在“工序识别”页设置。", _processEnabled, _processAssemblyAuto);
		groups.Controls.Add(process, 0, 1);
		groups.Controls.Add(FeatureGroup("一键爆炸", "按功能名“一键爆炸”绑定到轮盘；仅在手动激发时执行。", _explodeEnabled), 1, 1);
		panel.Controls.Add(new Label { Text = "本插件保留轮盘、水印、工序和一键爆炸。修改后点击“应用并保存”生效。", Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), ForeColor = Color.FromArgb(78, 94, 108) });
		panel.Controls.Add(BuildUserMacroSection());
		panel.Controls.Add(groups);
		panel.Controls.Add(SectionTitle("功能开关"));
		UpdateFeatureSwitchDependencies();
		return panel;
	}

	private static Control FeatureGroup(string title, string hint, params Control[] controls)
	{
		var group = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12, 24, 12, 10), Margin = new Padding(0, 0, 12, 12) };
		var body = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = controls.Length + 1 };
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		for (int i = 0; i < controls.Length; i++) { controls[i].Margin = new Padding(0, 3, 0, 5); body.Controls.Add(controls[i], 0, i); }
		body.Controls.Add(new Label { Text = hint, Dock = DockStyle.Fill, Height = 48, Padding = new Padding(0, 7, 0, 0), ForeColor = Color.FromArgb(78, 94, 108) }, 0, controls.Length);
		group.Controls.Add(body); return group;
	}

	private static Control SettingOptions(params Control[] controls)
	{
		var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 8, 0, 10) };
		options.Controls.AddRange(controls); return options;
	}

	private Control BuildProcessRecognitionSection()
	{
		Panel panel = Page();
		Label hint = new Label
		{
			Text = "后台识别使用已加载零件，操作模型时暂停。结果可直接编辑，选择类别或组合后先暂存；“全部确定写入”按各行结果提交。工序名称、总焊/总装组合及类别可在下方编辑表中增删，保存后用于后续识别。",
			Dock = DockStyle.Top,
			Height = 72,
			Padding = new Padding(0, 8, 8, 4),
			ForeColor = Color.FromArgb(78, 94, 108)
		};

		panel.Controls.Add(Fields(
			Field("默认识别方式", _processDefaultMode),
			Field("工序汇总属性", _processSummaryProperty),
			Field("工序分隔符", _processSeparator),
			Field("表处识别方式", _processSurfaceMode),
			Field("表处属性名", _processSurfaceProperty),
			Field("未命中回退", _processSurfaceFallback),
			Field("材质表处映射", _processSurfaceRules),
			Field("工艺说明属性", _processCraftProperty),
			Field("板厚属性名", _processThicknessProperty),
			Field("折弯下刀数属性名", _processBendCountProperty),
			Field("内轮廓数属性名", _processCutoutCountProperty),
			Field("零件类型属性名", _processPartTypeProperty),
			Field("工序类型属性名", _processProcessTypeProperty),
			Field("备注属性名", _processRemarkProperty),
			Field("外加工属性名", _processOutsourceProperty),
			Field("去毛刺工序名称", _processDeburrName),
			Field("检验工序名称", _processInspectionName),
			Field("包装工序名称", _processPackagingName),
			Field("发货工序名称", _processShippingName),
			Field("补充固定工序", _processAdditionalProcesses),
			Field("装配后台每批数量", _processAssemblyBatchSize),
			Field("装配批次间隔 ms", _processAssemblyBatchDelay)));
		panel.Controls.Add(hint);
		panel.Controls.Add(SettingOptions(_processWatermark, _processCraftDescription, _processGeometryProperties,
			_processDeburr, _processInspection, _processInspectionAfterSurface, _processPackaging, _processShipping));
		var editRules = new Button { Text = "工序组、类型默认组与名称设置…", Dock = DockStyle.Top, Height = 34 };
		editRules.Click += (_, __) => {
			if (OwnedForms.OfType<ProcessRulesWindow>().Any()) { OwnedForms.OfType<ProcessRulesWindow>().First().Activate(); return; }
			var value = ProcessRulesWindow.Copy(_config.ProcessRecognition);
			value.ProcessTypeGroups = _processTypeGroups; value.ProcessCategories = _processCategories.Text; value.ProcessCombinations = _processCombinations.Text; value.ProcessNameMappings = _processNameMappings.Text;
			var dialog = new ProcessRulesWindow(value);
			dialog.ShowModeless(this, result => { if (result == DialogResult.OK) { _processTypeGroups=dialog.Result.ProcessTypeGroups; _processCategories.Text=dialog.Result.ProcessCategories; _processCombinations.Text=dialog.Result.ProcessCombinations; _processNameMappings.Text=dialog.Result.ProcessNameMappings; } });
		};
		panel.Controls.Add(editRules);
		panel.Controls.Add(SectionTitle("工序识别与工艺属性"));
		return panel;
	}

	private Control BuildCommandSection()
	{
		Panel panel = Page();
		TableLayoutPanel toolbar = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 76,
			ColumnCount = 1,
			RowCount = 2
		};
		toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		FlowLayoutPanel editActions = ToolbarRow();
		editActions.Controls.Add(MakeButton("删除选中", DeleteRows, 76));
		editActions.Controls.Add(MakeButton("修改显示名", RenameCurrentCell, 98));
		editActions.Controls.Add(MakeButton("SW 基础功能", PickCurrentBasicAction, 118));
		editActions.Controls.Add(MakeButton("组合功能", PickCurrentComboAction, 98, primary: true));
		editActions.Controls.Add(MakeButton("录制步骤", RecordStepsForCurrentCell, 98));
		editActions.Controls.Add(MakeButton("其他插件命令", CaptureCommandForCurrentCell, 132));
		FlowLayoutPanel readActions = ToolbarRow();
		readActions.Controls.Add(MakeButton("打开命令库", () => RenderSection("hotkeys"), 130));
		toolbar.Controls.Add(editActions, 0, 0);
		toolbar.Controls.Add(readActions, 0, 1);
		panel.Controls.Add(_grid);
		panel.Controls.Add(toolbar);
		panel.Controls.Add(SectionTitle("格子命令"));
		return panel;
	}

	private Control BuildHotkeySection()
	{
		Panel panel = Page();
		TableLayoutPanel toolbar = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 78,
			ColumnCount = 1,
			RowCount = 2
		};
		toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		FlowLayoutPanel actions = ToolbarRow();
		actions.Controls.Add(MakeButton("读取SW命令", ReadSolidWorksCommands, 112, primary: true));
		actions.Controls.Add(MakeButton("读取快捷键", ReadKeyboardList, 108));
		actions.Controls.Add(MakeButton("读取外部功能", ReadQinJianFunctions, 132));
		actions.Controls.Add(MakeButton("读取扩展功能", ReadJiaLiChuangFunctions, 112));
		actions.Controls.Add(MakeButton("读取外部功能", ReadThirdPartyPluginFunctions, 126));
		UpdateSelectedCellText();

		TableLayoutPanel searchRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1
		};
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		searchRow.Controls.Add(new Label
		{
			Text = "搜索",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		_shortcutSearch.Dock = DockStyle.Fill;
		_shortcutSearch.Margin = new Padding(0, 5, 8, 4);
		searchRow.Controls.Add(_shortcutSearch, 1, 0);
		_selectedCellLabel.Padding = new Padding(4, 7, 4, 0);
		searchRow.Controls.Add(_selectedCellLabel, 2, 0);
		toolbar.Controls.Add(actions, 0, 0);
		toolbar.Controls.Add(searchRow, 0, 1);
		panel.Controls.Add(BuildCommandCatalogPane());
		panel.Controls.Add(toolbar);
		panel.Controls.Add(SectionTitle("命令库"));
		RefreshShortcutRows();
		return panel;
	}

	private Control BuildCommandCatalogPane()
	{
		SplitContainer split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			FixedPanel = FixedPanel.Panel1,
			SplitterDistance = 210,
			SplitterWidth = 5,
			BackColor = Color.FromArgb(226, 231, 236)
		};
		split.Panel1.BackColor = Color.White;
		split.Panel1.Padding = new Padding(8);
		split.Panel2.BackColor = Color.White;
		split.Panel1.Controls.Add(_commandCategoryTree);
		split.Panel2.Controls.Add(_shortcutGrid);
		StabilizeCatalogSplitter(split, 220);
		RefreshCommandCategoryTree();
		return split;
	}

	private static void StabilizeCatalogSplitter(SplitContainer split, int desiredWidth)
	{
		split.SizeChanged += delegate
		{
			if (split.Width > desiredWidth + 320 && split.SplitterDistance < desiredWidth)
			{
				split.SplitterDistance = desiredWidth;
			}
		};
	}

	private Control BuildAppearanceSection()
	{
		Panel panel = Page();
		TableLayoutPanel backgroundRow = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 38,
			ColumnCount = 4,
			RowCount = 1
		};
		backgroundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
		backgroundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		backgroundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
		backgroundRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106f));
		backgroundRow.Controls.Add(new Label
		{
			Text = "背景图片",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		_backgroundPath.Dock = DockStyle.Fill;
		_backgroundPath.Margin = new Padding(0, 5, 4, 4);
		backgroundRow.Controls.Add(_backgroundPath, 1, 0);
		backgroundRow.Controls.Add(MakeButton("选图", PickBackground, 76), 2, 0);
		backgroundRow.Controls.Add(MakeButton("清除背景", ClearBackground, 98), 3, 0);
		RebuildRingEditor();
		Label hint = new Label
		{
			Text = "每一圈按外边界直径（px）设置，并以鼠标位置为共同圆心；最外圈直径与轮盘直径同步。勾选“各圈等宽”时每圈径向宽度由圈数均分，直径自动给出；取消勾选可逐圈手调。轮盘字体、当前格字号和本页全部格的自动适配在“字体与适配”页设置。",
			Dock = DockStyle.Top,
			Height = 52,
			ForeColor = Color.FromArgb(92, 108, 122),
			Padding = new Padding(0, 8, 0, 0)
		};
		panel.Controls.Add(hint);
		panel.Controls.Add(_ringEditor);
		panel.Controls.Add(_equalRingBands);
		panel.Controls.Add(Fields(
			Field("轮盘直径 px", _menuSize),
			Field("中心圆半径 px", _centerRadius),
			Field("轮盘圈数", _ringCount),
			Field("背景透明 %", BuildBackgroundOpacityControl())));
		panel.Controls.Add(backgroundRow);
		panel.Controls.Add(SectionTitle("外观"));
		// Wheel actions moved off the preview column: they configure the wheel,
		// not the preview itself, so they belong with the appearance section.
		panel.Controls.Add(BuildWheelAppearanceTools());
		return panel;
	}

	private Control BuildFontSection()
	{
		Panel panel = Page();
		Label hint = new Label
		{
			Text = "选择轮盘格后可单独设置字号、字体和颜色；字号勾选“自动适配格子”时按每格的可用空间调整。右侧按钮可将字体、颜色或自动适配应用到本页全部已配置格。",
			Dock = DockStyle.Top,
			Height = 64,
			Padding = new Padding(0, 8, 8, 4),
			ForeColor = Color.FromArgb(78, 94, 108)
		};
		panel.Controls.Add(hint);
		panel.Controls.Add(Fields(
			Field("当前格字号 px", BuildCellFontSizeEditor()),
			Field("当前格字体", BuildCellFontNameEditor()),
			Field("当前格文字颜色", BuildCellTextColorEditor())));
		panel.Controls.Add(SectionTitle("轮盘字体与自适应"));
		LoadSelectedCellFontAppearance();
		return panel;
	}

	private Control BuildCellFontSizeEditor()
	{
		FlowLayoutPanel panel = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty
		};
		panel.Controls.Add(_fontSize);
		panel.Controls.Add(_autoFitCellFont);
		Button autoFitAll = MakeButton("全部自适应", ApplyAutoFitToAllCells, 104);
		panel.Controls.Add(autoFitAll);
		ToolTip autoFitTip = new ToolTip();
		autoFitTip.SetToolTip(autoFitAll, "清除本页已配置格子的固定字号，让每格按可用空间自动适配。");
		return panel;
	}

	private Control BuildCellFontNameEditor()
	{
		FlowLayoutPanel panel = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty
		};
		panel.Controls.Add(_fontName);
		panel.Controls.Add(MakeButton("应用字体到全部格", ApplyFontNameToAllCells, 132));
		return panel;
	}

	private Control BuildCellTextColorEditor()
	{
		FlowLayoutPanel panel = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty
		};
		panel.Controls.Add(_cellTextColor);
		panel.Controls.Add(MakeButton("应用颜色到全部格", ApplyTextColorToAllCells, 132));
		return panel;
	}

	private Control BuildAboutPage()
	{
		Panel page = Page();
		var details = new TableLayoutPanel
		{
			Name = "AboutDetails", Dock = DockStyle.Top, AutoSize = true,
			ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 8, 0, 12)
		};
		details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int row = 0; row < details.RowCount; row++) details.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		details.Controls.Add(AboutLabel("作者"), 0, 0);
		details.Controls.Add(AboutValue("XY"), 1, 0);
		details.Controls.Add(AboutLabel("联系"), 0, 1);
		details.Controls.Add(AboutValue("微信联系与抖音账号"), 1, 1);
		var codes = new FlowLayoutPanel
		{
			Name = "ContactQrCodes", Dock = DockStyle.Top, AutoSize = true,
			WrapContents = true, Padding = new Padding(0, 6, 0, 12)
		};
		codes.Controls.Add(AboutQrCard("微信", "XY.Contact.WeChat.jpg"));
		codes.Controls.Add(AboutQrCard("抖音", "XY.Contact.Douyin.jpg"));
		page.Controls.Add(codes);
		page.Controls.Add(details);
		page.Controls.Add(SectionTitle("关于与联系"));
		return page;
	}

	private static Control AboutQrCard(string caption, string resource)
	{
		var card = new GroupBox
		{
			Text = caption, Width = 252, Height = 280,
			Padding = new Padding(14, 26, 14, 14), Margin = new Padding(0, 0, 18, 12)
		};
		var picture = new PictureBox
		{
			Name = resource, AccessibleName = caption + "二维码", Dock = DockStyle.Fill,
			SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White
		};
		using (Stream stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream(resource))
		{
			if (stream == null) throw new InvalidOperationException("缺少联系二维码资源：" + caption);
			using (Image image = Image.FromStream(stream)) picture.Image = new Bitmap(image);
		}
		picture.Disposed += (_, __) => picture.Image?.Dispose();
		card.Controls.Add(picture);
		return card;
	}

	private static Label AboutLabel(string text)
	{
		return new Label
		{
			Text = text,
			Dock = DockStyle.Fill,
			Padding = new Padding(12, 10, 8, 8),
			ForeColor = Color.FromArgb(38, 54, 70),
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
		};
	}

	private static Label AboutValue(string text)
	{
		return new Label
		{
			Text = text,
			Dock = DockStyle.Fill,
			Padding = new Padding(12, 10, 8, 8),
			ForeColor = Color.FromArgb(68, 78, 88),
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular)
		};
	}

	private Control BuildHelpPage()
	{
		Panel page = Page();
		FlowLayoutPanel list = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			AutoScroll = true,
			Padding = new Padding(0, 8, 12, 18),
			BackColor = Color.FromArgb(248, 250, 252)
		};
		(string Title, string Detail)[] highlights =
		{
			("分场景轮盘", "空白页、零件、装配体、工程图与草图页分别保存，进入草图时可自动切换。"),
			("水印与工序", "水印按当前文档和选中零件刷新；工序在原生右侧任务窗格识别、编辑并确认写入。"),
			("命令直接读取", "综合读取可直接执行的 SOLIDWORKS 原生命令、快捷键和外部功能。"),
			("显示名与搜索", "修改显示名只改变轮盘文字和轮盘内搜索别名，不修改 SOLIDWORKS 或外部功能的原始命令。"),
			("文字样式", "默认使用宋体；字号、字体和颜色可设置当前格，也可一键应用到本页全部已配置格。"),
				("组合执行", "命令按保存顺序衔接；能判断完成时自动进入下一步，无法可靠判断时可人工确认。"),
				("备份与排错", "轮盘配置、背景资源和 SOLIDWORKS 自定义快捷键可选备份；异常写入插件目录 error.log。")
		};
		(string Title, string Detail)[] details =
		{
			("触发与选择", "在 SOLIDWORKS 中按住设置的触发键，滑向目标格并松开执行；在设置预览中单击格子可定位对应命令行。"),
			("命令读取与分类", "读取到的原生命令按草图、特征、钣金等菜单层级归类；快捷键按 SOLIDWORKS、外部功能来源和原类别整理，不能直接执行的项目自动跳过。"),
			("字号适配", "勾选自动适配时，按当前样式每格的可用图文空间缩小字号，最低 8 逻辑像素；过长名称在格内省略，悬停可查看全名。固定字号保留输入值，切换样式不改文字配置。"),
			("字体与颜色", "先在预览或格子表中选中目标格，再选择字体或颜色；“应用到全部格”只作用于当前轮盘页内已经配置功能的格子。"),
			("显示名改名", "在“格子命令”页选中功能后点击“修改显示名”。搜索既支持原命令名，也支持显示别名；执行仍使用原快捷键、按钮文本或命令 ID。"),
			("组合与衔接", "自动确定和延时核查互斥。未勾选自动确定时，先正常判断当前命令是否结束；需要人工控制时，可采用衔接键，手动进行下一步。"),
				("多文件功能", "需要连续处理多个文件的功能，建议启用衔接键，待全部文件处理完成后再人工进入下一步。"),
				("备份恢复", "导出、导入时可分别选择轮盘配置和 SOLIDWORKS 自定义快捷键；快捷键导入前自动生成回滚文件，完成后弹出 TXT 核对结果。"),
			("错误日志", "正常运行不生成错误报告；功能失败、异常或插件冲突时，在安装目录“插件文件”内生成 error.log。")
		};
		list.Controls.Add(new Label
		{
			Text = "可采用衔接键，手动进行下一步。",
			Width = 850,
			Height = 38,
			Padding = new Padding(12, 9, 8, 6),
			Margin = new Padding(0, 0, 0, 8),
			BackColor = Color.FromArgb(232, 243, 253),
			ForeColor = Color.FromArgb(35, 88, 140),
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
		});
		AddHelpGroup(list, "重点功能", highlights);
		AddHelpGroup(list, "操作与规则", details);
		page.Controls.Add(list);
		page.Controls.Add(SectionTitle("功能说明"));
		return page;
	}

	private static void AddHelpGroup(FlowLayoutPanel list, string heading, IEnumerable<(string Title, string Detail)> items)
	{
		list.Controls.Add(new Label
		{
			Text = heading,
			Width = 810,
			Height = 32,
			Padding = new Padding(2, 6, 0, 4),
			Margin = Padding.Empty,
			ForeColor = Color.FromArgb(38, 54, 70),
			Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
		});
		TableLayoutPanel table = new TableLayoutPanel
		{
			Width = 850,
			AutoSize = true,
			ColumnCount = 2,
			Margin = new Padding(0, 0, 0, 14),
			BackColor = Color.White,
			CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
		};
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		int row = 0;
		foreach ((string title, string detail) in items)
		{
			table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			table.Controls.Add(new Label
			{
				Text = title,
				Dock = DockStyle.Fill,
				AutoSize = true,
				MinimumSize = new Size(0, 46),
				Padding = new Padding(12, 12, 8, 10),
				Margin = Padding.Empty,
				BackColor = row % 2 == 0 ? Color.FromArgb(244, 247, 250) : Color.FromArgb(249, 251, 253),
				ForeColor = Color.FromArgb(39, 55, 70),
				Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
			}, 0, row);
			table.Controls.Add(new Label
			{
				Text = detail,
				Dock = DockStyle.Fill,
				AutoSize = true,
				MinimumSize = new Size(0, 46),
				Padding = new Padding(12, 12, 12, 10),
				Margin = Padding.Empty,
				BackColor = row % 2 == 0 ? Color.White : Color.FromArgb(252, 253, 254),
				ForeColor = Color.FromArgb(58, 70, 82)
			}, 1, row);
			row++;
		}
		list.Controls.Add(table);
	}

	private Control BuildBackgroundOpacityControl()
	{
		FlowLayoutPanel obj = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty
		};
		_backgroundOpacityValue.Text = _backgroundOpacity.Value + "%";
		obj.Controls.Add(_backgroundOpacity);
		obj.Controls.Add(_backgroundOpacityValue);
		return obj;
	}

	private Control BuildMenuOpacityControl()
	{
		FlowLayoutPanel obj = new FlowLayoutPanel
		{
			AutoSize = true,
			WrapContents = false,
			Margin = Padding.Empty
		};
		_menuOpacityValue.Text = _menuOpacity.Value + "%";
		obj.Controls.Add(_menuOpacity);
		obj.Controls.Add(_menuOpacityValue);
		return obj;
	}

	private static Panel Page()
	{
		return new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(248, 250, 252),
			AutoScroll = true
		};
	}

	private static Label SectionTitle(string text)
	{
		return new Label
		{
			Text = text,
			Dock = DockStyle.Top,
			Height = 38,
			ForeColor = Color.FromArgb(40, 52, 64),
			Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold)
		};
	}

	private static (string Label, Control Control) Field(string label, Control control)
	{
		return (Label: label, Control: control);
	}

	private static Control Fields(params (string Label, Control Control)[] fields)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			ColumnCount = 2,
			Padding = new Padding(0, 8, 0, 0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < fields.Length; i++)
		{
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
			tableLayoutPanel.Controls.Add(new Label
			{
				Text = fields[i].Label,
				AutoSize = true,
				Padding = new Padding(0, 7, 0, 0)
			}, 0, i);
			tableLayoutPanel.Controls.Add(fields[i].Control, 1, i);
		}
		return tableLayoutPanel;
	}

	private void ChangeScene()
	{
		if (!string.IsNullOrWhiteSpace(_activeSceneKey))
		{
			ApplyRows(_activeSceneKey);
		}
		LoadSceneRows();
	}

	private void BuildGrid()
	{
		_grid.Columns.Clear();
		StyleGrid(_grid);
		_grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		_grid.MultiSelect = true;
		_grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "Ring",
			HeaderText = "圈",
			Width = 54,
			ReadOnly = true
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "Slot",
			HeaderText = "格子",
			Width = 70,
			ReadOnly = true
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "Label",
			HeaderText = "显示名",
			FillWeight = 30f,
			AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
		});
		DataGridViewComboBoxColumn dataGridViewComboBoxColumn = new DataGridViewComboBoxColumn
		{
			Name = "Kind",
			HeaderText = "类型",
			Width = 110
		};
		dataGridViewComboBoxColumn.Items.Add("快捷键");
		dataGridViewComboBoxColumn.Items.Add("SW 基础功能");
		dataGridViewComboBoxColumn.Items.Add("SW 原生命令");
		dataGridViewComboBoxColumn.Items.Add("点击按钮");
		dataGridViewComboBoxColumn.Items.Add("外部功能");
		dataGridViewComboBoxColumn.Items.Add("外部插件功能");
		dataGridViewComboBoxColumn.Items.Add("扩展功能");
		dataGridViewComboBoxColumn.Items.Add("组合触发");
		dataGridViewComboBoxColumn.Items.Add("\u5feb\u6377\u952e\u5019\u9009");
		_grid.Columns.Add(dataGridViewComboBoxColumn);
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "Value",
			HeaderText = "值",
			FillWeight = 40f,
			AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "FontSize",
			HeaderText = "字号",
			Width = 62
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "FontName",
			HeaderText = "字体",
			Visible = false
		});
		_grid.Columns.Add(new DataGridViewTextBoxColumn
		{
			Name = "TextColor",
			HeaderText = "字颜色",
			Width = 86
		});
		_grid.CellDoubleClick += delegate(object _, DataGridViewCellEventArgs e)
		{
			if (e.RowIndex < 0)
			{
				return;
			}
			string name = _grid.Columns[e.ColumnIndex].Name;
			if (name == "Value")
			{
				PickValueForRow(e.RowIndex);
			}
			else if (name == "TextColor")
			{
				PickTextColorForRow(e.RowIndex);
			}
		};
		_grid.SelectionChanged += delegate
		{
			RememberSelectedCommandRow();
		};
		_grid.CellClick += delegate
		{
			RememberSelectedCommandRow();
		};
		_grid.CellFormatting += delegate(object _, DataGridViewCellFormattingEventArgs e)
		{
			if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "TextColor")
			{
				return;
			}
			string value = Convert.ToString(_grid.Rows[e.RowIndex].Cells["TextColor"].Value);
			string normalized = NormalizeColorText(value);
			if (normalized.Length == 0)
			{
				e.CellStyle.BackColor = Color.White;
				e.CellStyle.ForeColor = Color.FromArgb(35, 45, 55);
				return;
			}
			Color color = ColorTranslator.FromHtml(normalized);
			e.CellStyle.BackColor = color;
			e.CellStyle.ForeColor = IsDark(color) ? Color.White : Color.Black;
			e.Value = normalized;
		};
		_grid.DataError += delegate(object _, DataGridViewDataErrorEventArgs e)
		{
			e.ThrowException = false;
		};
	}

	private void BuildShortcutGrid()
	{
		_shortcutGrid.Columns.Clear();
		StyleGrid(_shortcutGrid);
		_shortcutGrid.Columns.Add(new DataGridViewImageColumn { Name = "Icon", HeaderText = "", Width = 28, ImageLayout = DataGridViewImageCellLayout.Zoom, ReadOnly = true });
		_shortcutGrid.Columns.Add("Hotkey", "快捷键/动作");
		_shortcutGrid.Columns.Add("Command", "命令");
		_shortcutGrid.Columns.Add("Category", "类别");
		_shortcutGrid.Columns[0].Width = 130;
		_shortcutGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
		_shortcutGrid.Columns[2].Width = 160;
		_shortcutGrid.DoubleClick += delegate
		{
			FillSelectedGridFromShortcut();
		};
	}

	private void RememberSelectedCommandRow()
	{
		if (!_loadingScene && _grid.CurrentRow != null && !_grid.CurrentRow.IsNewRow && TryReadGridRingSlot(_grid.CurrentRow, out var ring, out var slot))
		{
			_selectedCommandRing = ring;
			_selectedCommandSlot = slot;
			UpdateSelectedCellText();
			LoadSelectedCellFontAppearance();
		}
	}

	private void LoadSceneRows()
	{
		EndPreviewEditing();
		_loadingScene = true;
		_grid.Rows.Clear();
		_selectedCommandRing = null;
		_selectedCommandSlot = null;
		string text = CurrentSceneKey();
		SceneMenuConfig scene = GetScene(text);
		LoadSceneSwitches();
		_activeSceneKey = text;
		SetRingInputsFromScene(scene);
		SetAppearanceInputsFromScene(scene);
		_backgroundPath.Text = scene.BackgroundImagePath;
		_backgroundOpacity.Value = Math.Max(_backgroundOpacity.Minimum, Math.Min(_backgroundOpacity.Maximum, scene.BackgroundOpacityPercent));
		PopulateCommandRows(scene);
		_loadingScene = false;
		SelectCommandCell(1, 1);
		RefreshPreview();
	}

	private void ApplyRows()
	{
		ApplyRows(string.IsNullOrWhiteSpace(_activeSceneKey) ? CurrentSceneKey() : _activeSceneKey);
	}

	private void ApplyRows(string key)
	{
		if (!_loadingScene)
		{
			CommitGridEdits();
		}
		SceneMenuConfig scene = GetScene(key);
		scene.BackgroundImagePath = _backgroundPath.Text.Trim();
		scene.BackgroundOpacityPercent = _backgroundOpacity.Value;
		scene.MenuSizePx = (int)_menuSize.Value;
		scene.CenterRadiusPx = Math.Max(20, Math.Min(scene.MenuSizePx / 3, (int)_centerRadius.Value));
		scene.EqualRingBands = _equalRingBands.Checked;
		List<RingDefinition> ringDefinitionsFromInputs = GetRingDefinitionsFromInputs();
		if (!_loadingScene && (_grid.Rows.Count != ringDefinitionsFromInputs.Sum(d => d.Slots)
			|| _grid.Rows.Cast<DataGridViewRow>().Any(row => TryReadGridRingSlot(row, out int r, out int s)
				&& !ringDefinitionsFromInputs.Any(d => d.Index == r - 1 && s <= d.Slots))))
		{
			SyncCommandRowsToRingDefinitions();
		}
		// Disabled rings belong to the saved configuration, not the visible grid.
		var hiddenRings = scene.Rings.Where(r => !r.Enabled && !ringDefinitionsFromInputs.Any(d => d.Index == r.Index)).ToList();
		scene.Rings.Clear();
		foreach (RingDefinition item in ringDefinitionsFromInputs)
		{
			scene.Rings.Add(new RingConfig
			{
				Index = item.Index,
				Slots = item.Slots,
				SizeWeight = item.SizeWeight,
				DiameterPx = item.DiameterPx,
				Enabled = true
			});
		}
		scene.Rings.AddRange(hiddenRings);
		RingDiameterLayout.Normalize(scene);
		foreach (DataGridViewRow item2 in (IEnumerable)_grid.Rows)
		{
			if (item2.IsNewRow || !int.TryParse(Convert.ToString(item2.Cells["Ring"].Value), out var ringNumber) || !int.TryParse(Convert.ToString(item2.Cells["Slot"].Value), out var result))
			{
				continue;
			}
			RingConfig ringConfig = scene.Rings.FirstOrDefault((RingConfig r) => r.Index == ringNumber - 1);
			if (ringConfig != null)
			{
				int num = result - 1;
				string text = Convert.ToString(item2.Cells["Label"].Value)?.Trim() ?? "";
				string kind = DisplayToKind(Convert.ToString(item2.Cells["Kind"].Value));
				string text2 = Convert.ToString(item2.Cells["Value"].Value)?.Trim() ?? "";
				ResolveTypedGridCommand(ref text, ref kind, ref text2);
				int fontSizePx = ParseFontSize(Convert.ToString(item2.Cells["FontSize"].Value));
				string fontName = NormalizeFontName(Convert.ToString(item2.Cells["FontName"].Value));
				string textColor = NormalizeColorText(Convert.ToString(item2.Cells["TextColor"].Value));
				ComboRowState comboState = ReadComboRowState(item2.Tag);
				List<ComboCommandConfig> comboCommands = CloneComboCommands(comboState.Commands);
				if (kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(text2))
				{
					text2 = ComboSummary(comboCommands);
				}
				// A cell that has been placed or sized on the preview has no
				// command yet, but its geometry is real work the user did and
				// must survive the rebuild.
				bool hasGeometry = comboState.FreeX.HasValue && comboState.FreeY.HasValue
					|| comboState.CellSizePercent != 100 || comboState.CellShape != "Circle"
					|| comboState.IconRatioPercent != 55 || fontSizePx > 0 || fontName.Length > 0
					|| textColor.Length > 0 || comboState.CellBold.HasValue || !string.IsNullOrEmpty(comboState.CellEffect)
					|| !string.IsNullOrEmpty(comboState.ContentMode) || !string.IsNullOrEmpty(comboState.IconId)
					|| !string.IsNullOrEmpty(comboState.IconPath) || !string.IsNullOrEmpty(comboState.IconData);
				if (num >= 0 && num < ringConfig.Slots && (!string.IsNullOrWhiteSpace(text2) || comboCommands.Count > 0 || hasGeometry))
				{
					MenuItemConfig menuItemConfig = new MenuItemConfig
					{
						Slot = num,
						FontBold = comboState.CellBold, TextEffect = comboState.CellEffect, IconId = comboState.IconId, IconPath = comboState.IconPath, ContentMode = comboState.ContentMode,
						Label = (string.IsNullOrWhiteSpace(text) ? text2 : text),
						Kind = kind,
						Value = text2,
						FontSizePx = fontSizePx,
						FontName = fontName,
						TextColor = textColor,
						ComboAdvanceEnabled = comboState.AdvanceEnabled,
						ComboAdvanceKey = comboState.AdvanceKey,
						ComboCommands = comboCommands
					};
					comboState.CopyTo(menuItemConfig);
					ringConfig.Items.Add(menuItemConfig);
				}
			}
		}
	}

	/// <summary>
	/// Allows the command value cell to accept a catalog function name directly.
	/// Only command kinds that carry a resolvable identity participate; free-form
	/// button text, delays, and combo summaries retain their existing semantics.
	/// </summary>
	private void ResolveTypedGridCommand(ref string label, ref string kind, ref string value)
	{
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase)) return;
		string input = value.Length > 0 ? value : label;
		if (input.Length == 0 || LooksLikeHotkey(input)) return;
		if (!FunctionNameResolver.TryResolve(_shortcutRows, input, out KeyboardShortcutRow resolved, out _)) return;
		string resolvedKind = (resolved.ActionKind ?? "").Trim();
		if (resolvedKind.Length == 0) return;
		if (!kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase)
			&& !kind.Equals(resolvedKind, StringComparison.OrdinalIgnoreCase)) return;
		kind = resolvedKind;
		value = (resolved.Value ?? "").Trim();
		if (value.Length == 0) value = (resolved.Hotkey ?? "").Trim();
		if (label.Length == 0) label = (resolved.Command ?? "").Trim();
	}

	private static bool LooksLikeHotkey(string value)
	{
		string text = (value ?? "").Trim();
		if (text.IndexOf('+') >= 0 || text.Equals("Space", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Enter", StringComparison.OrdinalIgnoreCase) || text.Equals("Tab", StringComparison.OrdinalIgnoreCase)
			|| text.Equals("Esc", StringComparison.OrdinalIgnoreCase)) return true;
		return text.Length >= 2 && (text[0] == 'F' || text[0] == 'f') && int.TryParse(text.Substring(1), out _);
	}

	private void CommitGridEdits()
	{
		try
		{
			if (_grid.IsCurrentCellDirty)
			{
				_grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
			_grid.EndEdit();
		}
		catch
		{
		}
		try
		{
			if (_shortcutGrid.IsCurrentCellDirty)
			{
				_shortcutGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
			}
			_shortcutGrid.EndEdit();
		}
		catch
		{
		}
	}

	private void PopulateCommandRows(SceneMenuConfig scene)
	{
		List<RingDefinition> ringDefinitionsFromInputs = GetRingDefinitionsFromInputs();
		Dictionary<string, CommandRowData> saved = scene.Rings.SelectMany((RingConfig ring) => ring.Items.Select((MenuItemConfig item) => new CommandRowData(ring.Index + 1, item.Slot + 1, item.Label, KindToDisplay(item.Kind), item.Value, item.FontSizePx, item.ResolvedFontFamily(), item.TextColor, CloneComboCommands(item.ComboCommands), item.ComboAdvanceEnabled, item.ComboAdvanceKey, item.IconId, item.IconPath, item.ContentMode, item.FontBold, item.TextEffect, new ComboRowState { Commands = CloneComboCommands(item.ComboCommands) }.WithGeometryFrom(item)))).ToDictionary<CommandRowData, string, CommandRowData>((CommandRowData row) => CellKey(row.Ring, row.Slot), (CommandRowData row) => row, StringComparer.OrdinalIgnoreCase);
		PopulateCommandRows(ringDefinitionsFromInputs, saved);
	}

	private void SyncCommandRowsToRingDefinitions()
	{
		if (!_loadingScene)
		{
			int? selectedCommandRing = _selectedCommandRing;
			int? selectedCommandSlot = _selectedCommandSlot;
			CommitGridEdits();
			var definitions = GetRingDefinitionsFromInputs();
			// Changing the numeric circle/slot controls must retain displaced bindings,
			// just like removing a cell directly in the CAD preview.
			foreach (DataGridViewRow row in _grid.Rows)
			{
				if (!TryReadGridRingSlot(row, out int ring, out int slot)) continue;
				if (definitions.Any(d => d.Index == ring - 1 && slot <= d.Slots)) continue;
				MenuItemConfig displaced = UnassignedFromRow(row);
				if (displaced != null) GetScene(_activeSceneKey).UnassignedItems.Add(displaced);
			}
			Dictionary<string, CommandRowData> saved = SnapshotCommandRows();
			foreach (RingConfig hidden in GetScene(_activeSceneKey).Rings.Where(r => !r.Enabled))
			{
				foreach (MenuItemConfig item in hidden.Items)
				{
					string cell = CellKey(hidden.Index + 1, item.Slot + 1);
					if (!saved.ContainsKey(cell)) saved[cell] = new CommandRowData(hidden.Index + 1, item.Slot + 1,
						item.Label, KindToDisplay(item.Kind), item.Value, item.FontSizePx, item.ResolvedFontFamily(), item.TextColor,
						CloneComboCommands(item.ComboCommands), item.ComboAdvanceEnabled, item.ComboAdvanceKey,
						item.IconId, item.IconPath, item.ContentMode, item.FontBold, item.TextEffect,
						new ComboRowState().WithGeometryFrom(item));
				}
			}
			_loadingScene = true;
			_grid.Rows.Clear();
			PopulateCommandRows(definitions, saved);
			_loadingScene = false;
			if (!selectedCommandRing.HasValue || !selectedCommandSlot.HasValue || !SelectCommandCell(selectedCommandRing.Value, selectedCommandSlot.Value))
			{
				SelectCommandCell(1, 1);
			}
		}
	}

	private Dictionary<string, CommandRowData> SnapshotCommandRows()
	{
		Dictionary<string, CommandRowData> dictionary = new Dictionary<string, CommandRowData>(StringComparer.OrdinalIgnoreCase);
		foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
		{
			if (!item.IsNewRow && TryReadGridRingSlot(item, out var ring, out var slot))
			{
				ComboRowState state = ReadComboRowState(item.Tag);
				dictionary[CellKey(ring, slot)] = new CommandRowData(ring, slot, Convert.ToString(item.Cells["Label"].Value)?.Trim() ?? "", KindToDisplay(DisplayToKind(Convert.ToString(item.Cells["Kind"].Value))), Convert.ToString(item.Cells["Value"].Value)?.Trim() ?? "", ParseFontSize(Convert.ToString(item.Cells["FontSize"].Value)), NormalizeFontName(Convert.ToString(item.Cells["FontName"].Value)), NormalizeColorText(Convert.ToString(item.Cells["TextColor"].Value)), CloneComboCommands(state.Commands), state.AdvanceEnabled, state.AdvanceKey, state.IconId, state.IconPath, state.ContentMode, state.CellBold, state.CellEffect, state);
			}
		}
		return dictionary;
	}

	private void PopulateCommandRows(IEnumerable<RingDefinition> definitions, IReadOnlyDictionary<string, CommandRowData> saved)
	{
		foreach (RingDefinition definition in definitions)
		{
			for (int i = 1; i <= definition.Slots; i++)
			{
				saved.TryGetValue(CellKey(definition.Index + 1, i), out var value);
				int rowIndex = _grid.Rows.Add(definition.Index + 1, i, value.Label ?? "", string.IsNullOrWhiteSpace(value.Kind) ? "快捷键" : value.Kind, value.Value ?? "", value.FontSizePx <= 0 ? "" : value.FontSizePx.ToString(), value.FontName ?? "", value.TextColor ?? "");
				ComboRowState rowState = new ComboRowState
				{
					CellBold = value.CellBold, CellEffect = value.CellEffect, IconId = value.IconId, IconPath = value.IconPath, ContentMode = value.ContentMode,
					Commands = CloneComboCommands(value.ComboCommands),
					AdvanceEnabled = value.ComboAdvanceEnabled,
					AdvanceKey = ComboAdvanceKeys.Normalize(value.ComboAdvanceKey)
				};
				rowState.CopyFrom(value.CellState);
				_grid.Rows[rowIndex].Tag = rowState;
			}
		}
	}

	private string CurrentSceneKey()
	{
		return (_scene.SelectedItem as SceneChoice)?.Key ?? "sw.blank";
	}

	private SceneMenuConfig GetScene(string key)
	{
		if (!_config.Menus.TryGetValue(key, out SceneMenuConfig value))
		{
			value = new SceneMenuConfig
			{
				Key = key,
				Rings = new List<RingConfig>
				{
					new RingConfig
					{
						Index = 0,
						Slots = 8
					}
				}
			};
			_config.Menus[key] = value;
		}
		return value;
	}

	private void AddRow()
	{
		DataGridViewRow dataGridViewRow = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault((DataGridViewRow row) => !row.IsNewRow && string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["Value"].Value)));
		if (dataGridViewRow != null)
		{
			_grid.CurrentCell = dataGridViewRow.Cells["Label"];
			RememberSelectedCommandRow();
		}
	}

	private void DeleteRows()
	{
		foreach (DataGridViewRow item in SelectedCommandRows())
		{
			ClearCommandRow(item);
		}
		RefreshPreview();
	}

	private void PickBackground()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "选择轮盘背景图",
			Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			_backgroundPath.Text = openFileDialog.FileName;
		}
	}

	private void ClearBackground()
	{
		_backgroundPath.Clear();
	}

	private void ClearCurrentGrid()
	{
		List<DataGridViewRow> list = SelectedCommandRows().ToList();
		if (list.Count == 0 && _grid.CurrentRow != null && !_grid.CurrentRow.IsNewRow)
		{
			list.Add(_grid.CurrentRow);
		}
		foreach (DataGridViewRow item in list)
		{
			ClearCommandRow(item);
		}
		RefreshPreview();
	}

	private async void RenameCurrentCell()
	{
		DataGridViewRow row = FindSelectedCommandRow();
		if (!IsOccupiedCommandRow(row))
		{
			ToolNotice.Show(this, "请先选择已经设置功能的轮盘格。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		string current = Convert.ToString(row.Cells["Label"].Value)?.Trim() ?? "";
		string renamed = await PromptForDisplayName(current);
		if (renamed == null || IsDisposed || row.DataGridView != _grid)
		{
			return;
		}
		row.Cells["Label"].Value = renamed;
		RefreshPreview();
		RefreshShortcutRows();
	}

	private async System.Threading.Tasks.Task<string> PromptForDisplayName(string current)
	{
		using PluginToolWindow dialog = new PluginToolWindow
		{
			Text = "修改轮盘显示名",
			StartPosition = FormStartPosition.CenterParent,
			Width = 430,
			Height = 164,
			MinimizeBox = false,
			MaximizeBox = false,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			BackColor = Color.White,
			Font = Font
		};
		TextBox input = new TextBox
		{
			Text = current,
			Dock = DockStyle.Top,
			Margin = new Padding(12),
			MaxLength = 80
		};
		Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
		Label label = new Label
		{
			Text = "轮盘显示名",
			Dock = DockStyle.Top,
			Height = 28,
			TextAlign = ContentAlignment.MiddleLeft
		};
		FlowLayoutPanel footer = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			FlowDirection = FlowDirection.RightToLeft,
			Padding = new Padding(8)
		};
		Button confirm = MakeButton("确定", delegate
		{
			if (!string.IsNullOrWhiteSpace(input.Text))
			{
				dialog.DialogResult = DialogResult.OK;
				dialog.Close();
			}
		}, 82, primary: true);
		Button cancel = MakeButton("取消", delegate
		{
			dialog.DialogResult = DialogResult.Cancel;
			dialog.Close();
		}, 82);
		footer.Controls.Add(confirm);
		footer.Controls.Add(cancel);
		body.Controls.Add(input);
		body.Controls.Add(label);
		dialog.Controls.Add(body);
		dialog.Controls.Add(footer);
		dialog.AcceptButton = confirm;
		dialog.CancelButton = cancel;
		dialog.Shown += delegate
		{
			input.Focus();
			input.SelectAll();
		};
		return await dialog.ShowModelessAsync(this) == DialogResult.OK ? input.Text.Trim() : null;
	}

	private void ClearCurrentWheel()
	{
		string sceneName = SceneKeys.Display(CurrentSceneKey());
		if (MessageBox.Show(this, "确定清空“" + sceneName + "”当前轮盘的全部格子吗？", DisplayName(), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
		{
			return;
		}
		foreach (DataGridViewRow row in _grid.Rows)
		{
			if (!row.IsNewRow)
			{
				ClearCommandRow(row);
			}
		}
		_selectedCommandRing = null;
		_selectedCommandSlot = null;
		UpdateSelectedCellText();
		SelectCommandCell(1, 1);
		RefreshPreview();
	}

	private void SelectScene(string key)
	{
		foreach (SceneChoice item in _scene.Items)
		{
			if (item.Key == key)
			{
				_scene.SelectedItem = item;
				break;
			}
		}
	}

	private void PickCurrentBasicAction()
	{
		DataGridViewRow dataGridViewRow = _grid.CurrentRow;
		if (dataGridViewRow == null || dataGridViewRow.IsNewRow)
		{
			AddRow();
			dataGridViewRow = _grid.Rows[_grid.Rows.Count - 1];
		}
		dataGridViewRow.Cells["Kind"].Value = "SW 基础功能";
		PickValueForRow(dataGridViewRow.Index);
	}

	private string ResolveCurrentSolidWorksScene()
	{
		try
		{
			ISldWorks solidWorks = _getSolidWorks();
			IModelDoc2 document = solidWorks?.ActiveDoc as IModelDoc2;
			if (document == null)
			{
				return SceneKeys.Blank;
			}
			return SceneResolver.Resolve(_config, document.GetType(), SceneResolver.IsEditingSketch(document));
		}
		catch
		{
			return SceneKeys.Blank;
		}
	}

	private void PickCurrentComboAction()
	{
		DataGridViewRow dataGridViewRow = _grid.CurrentRow;
		if (dataGridViewRow == null || dataGridViewRow.IsNewRow)
		{
			AddRow();
			dataGridViewRow = _grid.CurrentRow ?? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault((DataGridViewRow row) => !row.IsNewRow);
		}
		if (dataGridViewRow == null || dataGridViewRow.IsNewRow)
		{
			return;
		}
		dataGridViewRow.Cells["Kind"].Value = "组合触发";
		PickComboForRow(dataGridViewRow);
	}

	// Records the commands clicked in SOLIDWORKS straight onto the selected cell.
	// Works for a plain function too: the cell keeps its own kind and simply gains
	// an ordered sequence to run.

    private void CaptureCommandForCurrentCell()
    {
        DataGridViewRow row = _grid.CurrentRow;
        if (row == null || row.IsNewRow) { ToolNotice.Show(this, "请先选择要设置的轮盘格子。", DisplayName()); return; }
        if (_getSolidWorks() == null) { ToolNotice.Show(this, "请在 SOLIDWORKS 内打开设置后获取命令。", DisplayName()); return; }
        _grid.EndEdit();
        string sceneKey = CurrentSceneKey();
        object[] snapshot = row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value).ToArray();
        object previousTag = row.Tag;
        CommandCaptureWindow capture = new CommandCaptureWindow(_getSolidWorks, _getCommandManager);
        capture.ShowModeless(this, result =>
        {
            if (result != DialogResult.OK || IsDisposed || capture.Captured == null) return;
            if (row.DataGridView != _grid || sceneKey != CurrentSceneKey()
                || !snapshot.SequenceEqual(row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value))
                || !ReferenceEquals(previousTag, row.Tag))
            {
                ToolNotice.Show(this, "原轮盘格子已变化，请重新选择格子后获取，未覆盖现有内容。", DisplayName());
                return;
            }
            ComboCommandConfig command = capture.Captured;
            ComboRowState state = ReadComboRowState(row.Tag);
            state.Commands.Clear();
            state.AdvanceEnabled = false;
            row.Tag = state;
            row.Cells["Kind"].Value = KindToDisplay(command.Kind);
            row.Cells["Value"].Value = command.Value;
            row.Cells["Label"].Value = command.Label;
            RefreshPreview();
            ApplyEditorToConfig();
        });
    }

	private void RecordStepsForCurrentCell()
	{
		DataGridViewRow row = _grid.CurrentRow;
		if (row == null || row.IsNewRow)
		{
			AddRow();
			row = _grid.CurrentRow ?? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault((DataGridViewRow item) => !item.IsNewRow);
		}
		if (row == null || row.IsNewRow) return;
		if (_getSolidWorks() == null)
		{
			ToolNotice.Show(this, "请在 SOLIDWORKS 内打开设置后再录制。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		ComboRowState recordingState = ReadComboRowState(row.Tag);
		CommandRecorderWindow recorder = new CommandRecorderWindow(recordingState.Commands);
		recorder.ShowModeless(this, delegate (DialogResult result)
		{
			if (!IsDisposed) Enabled = true;
			if (result != DialogResult.OK || IsDisposed || row.DataGridView != _grid || recorder.Recorded.Count == 0) return;
			ComboRowState state = ReadComboRowState(row.Tag);
			state.Commands = CloneComboCommands(recorder.Recorded);
			state.AdvanceEnabled = state.AdvanceEnabled || state.Commands.Any(command => command.RequireAdvanceKey);
			row.Cells["Kind"].Value = KindToDisplay(ActionKinds.Combo);
			row.Tag = state;
			row.Cells["Value"].Value = ComboSummary(state.Commands);
			if (string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["Label"].Value)))
			{
				row.Cells["Label"].Value = "录制步骤";
			}
			RefreshPreview();
			ApplyEditorToConfig();
		});
		Enabled = false;
	}

	private async void PickValueForRow(int rowIndex)
	{
		DataGridViewRow dataGridViewRow = _grid.Rows[rowIndex];
		string kind = DisplayToKind(Convert.ToString(dataGridViewRow.Cells["Kind"].Value));
		if (kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
		{
			PickComboForRow(dataGridViewRow);
			return;
		}
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			using (BasicActionPicker basicActionPicker = new BasicActionPicker())
			{
				if (await basicActionPicker.ShowModelessAsync(this) == DialogResult.OK && dataGridViewRow.DataGridView == _grid)
				{
					dataGridViewRow.Cells["Value"].Value = basicActionPicker.SelectedValue;
					if (string.IsNullOrWhiteSpace(Convert.ToString(dataGridViewRow.Cells["Label"].Value)))
					{
						dataGridViewRow.Cells["Label"].Value = BasicActions.Display(basicActionPicker.SelectedValue);
					}
				}
				return;
			}
		}
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			ToolNotice.Show(this, "在“值”里输入要点击的 SOLIDWORKS 按钮文字，也可以先读取外部功能后双击填入。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			ReadQinJianFunctions();
			return;
		}
		if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			ReadJiaLiChuangFunctions();
			return;
		}
		ReadKeyboardList();
	}

	private async void PickComboForRow(DataGridViewRow row)
	{
		ComboRowState state = ReadComboRowState(row.Tag);
		using ComboCommandEditor editor = new ComboCommandEditor(
			Convert.ToString(row.Cells["Label"].Value),
			CloneComboCommands(state.Commands),
			_shortcutRows,
			_getSolidWorksWindowHandle,
			state.AdvanceEnabled,
			state.AdvanceKey,
			_globalComboAdvance.Checked,
			_getSolidWorks,
			_getCommandManager,
			PreferredCommandAlias);
		if (await editor.ShowModelessAsync(this) == DialogResult.OK && row.DataGridView == _grid)
		{
			List<ComboCommandConfig> commands = editor.Commands;
			_shortcutRows = editor.ShortcutRows.ToList();
			ComboRowState updated = new ComboRowState
			{
				CellBold = state.CellBold, CellEffect = state.CellEffect, IconId = state.IconId, IconPath = state.IconPath, ContentMode = state.ContentMode,
				Commands = CloneComboCommands(commands),
				AdvanceEnabled = editor.ComboAdvanceEnabled,
				AdvanceKey = editor.ComboAdvanceKey
			};
			updated.CopyFrom(state);
			row.Tag = updated;
			row.Cells["Kind"].Value = "组合触发";
			row.Cells["Value"].Value = ComboSummary(commands);
			row.Cells["Label"].Value = editor.ComboName;
			RefreshPreview();
			ApplyEditorToConfig();
			PersistEditorConfig();
		}
	}

	private void PickTextColorForRow(int rowIndex)
	{
		DataGridViewRow dataGridViewRow = _grid.Rows[rowIndex];
		using ColorDialog colorDialog = new ColorDialog
		{
			FullOpen = true,
			Color = ParseColorOrDefault(Convert.ToString(dataGridViewRow.Cells["TextColor"].Value), Color.FromArgb(18, 28, 38))
		};
		if (colorDialog.ShowDialog(this) == DialogResult.OK)
		{
			dataGridViewRow.Cells["TextColor"].Value = ColorTranslator.ToHtml(Color.FromArgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B));
			RefreshPreview();
		}
	}

	private static int ParseFontSize(string value)
	{
		if (!int.TryParse((value ?? "").Trim(), out int result))
		{
			return 0;
		}
		return Math.Max(0, Math.Min(72, result));
	}

	private static string NormalizeFontName(string value)
	{
		return (value ?? "").Trim();
	}

	private static string NormalizeColorText(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return "";
		}
		try
		{
			Color color = ColorTranslator.FromHtml(text);
			return ColorTranslator.ToHtml(Color.FromArgb(color.R, color.G, color.B));
		}
		catch
		{
			return "";
		}
	}

	private static Color ParseColorOrDefault(string value, Color fallback)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return fallback;
		}
		try
		{
			return ColorTranslator.FromHtml(text);
		}
		catch
		{
			return fallback;
		}
	}

	private static bool IsDark(Color color)
	{
		return color.R * 0.299 + color.G * 0.587 + color.B * 0.114 < 145;
	}

	private void ReadKeyboardList()
	{
		if (!KeyboardListReader.TryRead(out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Error("Read SOLIDWORKS keyboard shortcut list failed: " + message);
			ToolNotice.Show(this, message, "读取自定义键盘列表", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			SolidWorksNativeCommandCatalog.ClassifyShortcutRows(_getSolidWorks(), rows);
		}
		catch (Exception ex)
		{
			Logger.Trace("Classify SOLIDWORKS shortcuts by toolbar failed: " + ex.Message);
		}
		rows = SolidWorksNativeCommandCatalog.ExcludeSeparatelyReadPluginShortcuts(rows);
		ApplyRows(_activeSceneKey);
		IReadOnlyList<ShortcutChangeResult> changes = ShortcutChangeDetector.Compare(_config, rows);
		IReadOnlyList<ShortcutConflictResult> conflicts = ShortcutChangeDetector.FindConflicts(rows);
		int synchronized = ShortcutChangeDetector.Synchronize(_config, rows);
		if (synchronized > 0)
		{
			PersistEditorConfig();
			LoadSceneRows();
		}
		_shortcutRows = rows;
		ShortcutPaletteStore.Save(rows);
		RenderSection("hotkeys");
		RefreshShortcutRows();
		ShowShortcutChangeResults(changes, conflicts, synchronized);
	}

	private void ReadSolidWorksCommands()
	{
		ISldWorks solidWorks;
		try
		{
			solidWorks = _getSolidWorks();
		}
		catch (Exception ex)
		{
			Logger.Error("Get SOLIDWORKS window for native command read failed", ex);
			ToolNotice.Show(this, ex.Message, "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		ICommandManager commandManager = null;
		try
		{
			commandManager = _getCommandManager();
		}
		catch (Exception ex)
		{
			Logger.Trace("Get SOLIDWORKS CommandManager for native command read skipped: " + ex.Message);
		}
		if (!SolidWorksNativeCommandCatalog.TryRead(solidWorks, commandManager, out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Trace("Read SOLIDWORKS native command list skipped: " + message);
			ToolNotice.Show(this, message, "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		RenderSection("hotkeys");
		SelectCommandCategory("SOLIDWORKS");
		ToolNotice.Show(this, $"已读取 {rows.Count} 个已识别的 SOLIDWORKS 原生命令。实际能否执行取决于当前文档、选择和草图状态。可按左侧分类树浏览，双击填入轮盘格子。", "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ReadQinJianFunctions()
	{
		IntPtr solidWorksWindow = IntPtr.Zero;
		try
		{
			solidWorksWindow = _getSolidWorksWindowHandle();
		}
		catch (Exception ex)
		{
			Logger.Error("Get SOLIDWORKS window for QinJian read failed", ex);
		}
		if (!QinJianFunctionReader.TryRead(solidWorksWindow, out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Error("Read QinJian function list failed: " + message);
			ToolNotice.Show(this, SanitizeExternalMessage(message), "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		RenderSection("hotkeys");
		if (!_shortcutSearch.Text.Equals("外部功能", StringComparison.OrdinalIgnoreCase))
		{
			_shortcutSearch.Text = "外部功能";
		}
		else
		{
			RefreshShortcutRows();
		}
		ToolNotice.Show(this, $"已读取 {rows.Count} 个外部功能。双击列表中的功能，可填入当前选中的轮盘格子。", "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ReadJiaLiChuangFunctions()
	{
		if (!JiaLiChuangFunctionCatalog.TryRead(_getSolidWorks(), out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Error("Read JiaLiChuang Ican function list failed: " + message);
			ToolNotice.Show(this, SanitizeExternalMessage(message), "读取扩展功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		RenderSection("hotkeys");
		if (!_shortcutSearch.Text.Equals("扩展功能", StringComparison.OrdinalIgnoreCase))
		{
			_shortcutSearch.Text = "扩展功能";
		}
		else
		{
			RefreshShortcutRows();
		}
		ToolNotice.Show(this, $"已读取 {rows.Count} 个扩展功能。双击列表中的功能，可填入当前选中的轮盘格子。", "读取扩展功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ReadThirdPartyPluginFunctions()
	{
		if (!ExternalPluginCommandCatalog.TryRead(_getSolidWorks(), _getCommandManager(), out List<KeyboardShortcutRow> rows, out string message))
		{
			if (!QinJianFunctionReader.TryReadExternalPlugins(_getSolidWorksWindowHandle(), out rows, out string uiMessage))
			{
				ToolNotice.Show(this, SanitizeExternalMessage(uiMessage), "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			message = uiMessage;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		RenderSection("hotkeys");
		_shortcutSearch.Text = "外部功能";
		ToolNotice.Show(this, SanitizeExternalMessage(message) + " 双击列表功能可填入当前选中的轮盘格子。", "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private async void ShowShortcutChangeResults(IReadOnlyList<ShortcutChangeResult> changes, IReadOnlyList<ShortcutConflictResult> conflicts, int synchronized)
	{
		if ((changes == null || changes.Count == 0) && (conflicts == null || conflicts.Count == 0))
		{
			ToolNotice.Show(this, $"已读取 SOLIDWORKS 快捷键。未发现冲突或变化。已同步 {synchronized} 项。", "快捷键核对", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		using PluginToolWindow dialog = new PluginToolWindow
		{
			Text = "快捷键核对结果",
			StartPosition = FormStartPosition.CenterParent,
			Width = 820,
			Height = 460,
			MinimizeBox = false,
			MaximizeBox = false,
			BackColor = Color.White,
			Font = Font
		};
		Label title = new Label
		{
			Text = $"已同步 {synchronized} 项；发现 {conflicts?.Count ?? 0} 个快捷键冲突",
			Dock = DockStyle.Top,
			Height = 42,
			Padding = new Padding(12, 12, 12, 0),
			ForeColor = Color.FromArgb(40, 52, 64),
			Font = new Font(Font, FontStyle.Bold)
		};
		DataGridView grid = new DataGridView
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			RowHeadersVisible = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			MultiSelect = false,
			BackgroundColor = Color.White,
			BorderStyle = BorderStyle.None
		};
		grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "位置", Width = 230 });
		grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "命令", Width = 180 });
		grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ConfiguredHotkey", HeaderText = "当前配置值", Width = 130 });
		grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SolidWorksHotkey", HeaderText = "SOLIDWORKS 当前值", Width = 160 });
		grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "结果", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
		foreach (ShortcutChangeResult change in changes)
		{
			grid.Rows.Add(change.Location, change.Command, change.ConfiguredHotkey, change.SolidWorksHotkey, change.Status);
		}
		foreach (ShortcutConflictResult conflict in conflicts ?? Array.Empty<ShortcutConflictResult>())
		{
			grid.Rows.Add("SOLIDWORKS 自定义键盘", string.Join(" / ", conflict.Commands), "", conflict.Hotkey, "同一快捷键对应多个命令，未自动写入轮盘");
		}
		Button close = MakeButton("关闭", dialog.Close, 92, primary: true);
		FlowLayoutPanel footer = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 50,
			FlowDirection = FlowDirection.RightToLeft,
			Padding = new Padding(10)
		};
		footer.Controls.Add(close);
		dialog.Controls.Add(grid);
		dialog.Controls.Add(footer);
		dialog.Controls.Add(title);
		await dialog.ShowModelessAsync(this);
	}

	private void FillSelectedGridFromShortcut()
	{
		if (_shortcutGrid.CurrentRow?.Tag is KeyboardShortcutRow keyboardShortcutRow)
		{
			FillSelectedGridFromShortcut(keyboardShortcutRow);
		}
	}

	private void ResolveTypedFunctionForCurrentCell()
	{
		if (!FunctionNameResolver.TryResolve(_shortcutRows, _shortcutSearch.Text, out KeyboardShortcutRow row, out string message))
		{
			ToolNotice.Show(this, message, DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (FindSelectedCommandRow() == null)
		{
			ToolNotice.Show(this, "请先在“格子命令”里选中要填入的格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		FillSelectedGridFromShortcut(row);
	}

	private void FillSelectedGridFromShortcut(KeyboardShortcutRow keyboardShortcutRow)
	{
		if (keyboardShortcutRow != null)
		{
			string displayName = PreferredCommandAlias(keyboardShortcutRow);
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = keyboardShortcutRow.Command;
			}
			if (!CanAssignShortcut(keyboardShortcutRow))
			{
				return;
			}
			DataGridViewRow dataGridViewRow = FindSelectedCommandRow();
			if (dataGridViewRow == null)
			{
				ToolNotice.Show(this, "请先在“格子命令”里选中要填入的格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			if (DisplayToKind(Convert.ToString(dataGridViewRow.Cells["Kind"].Value)).Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
			{
				ComboRowState state = ReadComboRowState(dataGridViewRow.Tag);
				List<ComboCommandConfig> commands = CloneComboCommands(state.Commands);
				commands.Add(new ComboCommandConfig
				{
					Label = displayName,
					Kind = ShortcutActionKind(keyboardShortcutRow),
					Value = ShortcutActionValue(keyboardShortcutRow),
					AutoConfirm = false,
					// A command inserted into an existing sequence must expose the
					// same hand-off gate as a recorded step.  Otherwise the next
					// step can be skipped as soon as SOLIDWORKS posts the open event.
					RequireAdvanceKey = true
				});
				state.Commands = commands;
				dataGridViewRow.Tag = state;
				dataGridViewRow.Cells["Value"].Value = ComboSummary(commands);
				if (string.IsNullOrWhiteSpace(Convert.ToString(dataGridViewRow.Cells["Label"].Value)))
				{
					dataGridViewRow.Cells["Label"].Value = "组合触发";
				}
				RefreshPreview();
				return;
			}
			dataGridViewRow.Cells["Label"].Value = displayName;
			dataGridViewRow.Cells["Kind"].Value = KindToDisplay(ShortcutActionKind(keyboardShortcutRow));
			dataGridViewRow.Cells["Value"].Value = ShortcutActionValue(keyboardShortcutRow);
			// Hand-off from the icon library: the command brings its artwork along,
			// but a picture the user chose earlier is never replaced.
			ComboRowState filled = ReadComboRowState(dataGridViewRow.Tag);
			if (string.IsNullOrWhiteSpace(filled.IconPath)) filled.IconId = CatalogIconId(keyboardShortcutRow);
			dataGridViewRow.Tag = filled;
			LoadSelectedCellFontAppearance();
			RefreshPreview();
		}
	}

	private DataGridViewRow? FindSelectedCommandRow()
	{
		int? selectedCommandRing = _selectedCommandRing;
		if (selectedCommandRing.HasValue)
		{
			selectedCommandRing = _selectedCommandSlot;
			if (selectedCommandRing.HasValue)
			{
				foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
				{
					if (!item.IsNewRow && TryReadGridRingSlot(item, out var ring, out var slot) && ring == _selectedCommandRing.Value && slot == _selectedCommandSlot.Value)
					{
						return item;
					}
				}
			}
		}
		DataGridViewRow currentRow = _grid.CurrentRow;
		if (currentRow != null && !currentRow.IsNewRow && TryReadGridRingSlot(currentRow, out var _, out var _))
		{
			RememberSelectedCommandRow();
			return currentRow;
		}
		return null;
	}

	private static bool TryReadGridRingSlot(DataGridViewRow row, out int ring, out int slot)
	{
		ring = 0;
		slot = 0;
		if (int.TryParse(Convert.ToString(row.Cells["Ring"].Value), out ring) && int.TryParse(Convert.ToString(row.Cells["Slot"].Value), out slot) && ring > 0)
		{
			return slot > 0;
		}
		return false;
	}

	private bool SelectCommandCell(int ring, int slot)
	{
		foreach (DataGridViewRow item in (IEnumerable)_grid.Rows)
		{
			if (!item.IsNewRow && TryReadGridRingSlot(item, out var ring2, out var slot2) && ring2 == ring && slot2 == slot)
			{
				_grid.CurrentCell = item.Cells["Label"];
				item.Selected = true;
				_selectedCommandRing = ring;
				_selectedCommandSlot = slot;
				UpdateSelectedCellText();
				LoadSelectedCellFontAppearance();
				return true;
			}
		}
		return false;
	}

	private IEnumerable<DataGridViewRow> SelectedCommandRows()
	{
		List<DataGridViewRow> list = (from DataGridViewRow row in _grid.SelectedRows
			where !row.IsNewRow
			select row).ToList();
		if (list.Count > 0)
		{
			return list;
		}
		if (_grid.CurrentRow == null || _grid.CurrentRow.IsNewRow)
		{
			return Array.Empty<DataGridViewRow>();
		}
		return new DataGridViewRow[1] { _grid.CurrentRow };
	}

	private static void ClearCommandRow(DataGridViewRow row)
	{
		row.Cells["Label"].Value = "";
		row.Cells["Kind"].Value = "快捷键";
		row.Cells["Value"].Value = "";
		row.Cells["FontSize"].Value = "";
		row.Cells["FontName"].Value = "";
		row.Cells["TextColor"].Value = "";
		row.Tag = null;
	}

	private static string DisplayToKind(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Equals("\u5feb\u6377\u952e\u5019\u9009", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ShortcutPalette;
		}
		if (text.Equals("SW 基础功能", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.Basic;
		}
		if (text.Equals("SW 原生命令", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.NativeCommand;
		}
		if (text.Equals("点击按钮", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.UiClick;
		}
		if (text.Equals("外部功能", StringComparison.OrdinalIgnoreCase) || text.Equals("琴剑功能", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.QinJian;
		}
		if (text.Equals("外部插件功能", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ExternalPlugin;
		}
		if (text.Equals("扩展功能", StringComparison.OrdinalIgnoreCase) || text.Equals("嘉立创功能", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.JiaLiChuang;
		}
		if (text.Equals("组合触发", StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.Combo;
		}
		return ActionKinds.Hotkey;
	}

	private static string KindToDisplay(string value)
	{
		string kind = (value ?? "").Trim();
		if (kind.Equals(ActionKinds.ShortcutPalette, StringComparison.OrdinalIgnoreCase))
		{
			return "\u5feb\u6377\u952e\u5019\u9009";
		}
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			return "SW 基础功能";
		}
		if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			return "SW 原生命令";
		}
		if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			return "点击按钮";
		}
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			return "外部功能";
		}
		if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			return "外部插件功能";
		}
		if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			return "扩展功能";
		}
		if (kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
		{
			return "组合触发";
		}
		return "快捷键";
	}

	private static List<ComboCommandConfig> CloneComboCommands(IEnumerable<ComboCommandConfig>? source)
	{
		if (source == null)
		{
			return new List<ComboCommandConfig>();
		}
		return source
			.Where((ComboCommandConfig command) => command != null)
			.Select((ComboCommandConfig command) => new ComboCommandConfig
			{
				Label = command.Label ?? "",
				Kind = command.Kind ?? ActionKinds.Hotkey,
				Value = command.Value ?? "",
				AutoConfirm = command.AutoConfirm,
				RequireAdvanceKey = command.RequireAdvanceKey,
				AdvanceKey = command.AdvanceKey ?? "",
				WaitMode = command.WaitMode ?? ComboWaitModes.None,
				DelayMs = command.DelayMs,
				TimeoutMs = command.TimeoutMs,
				OnError = command.OnError ?? ComboErrorPolicies.Stop,
				RetryCount = command.RetryCount,
				SaveSelectionBefore = command.SaveSelectionBefore,
				RestoreSelectionAfter = command.RestoreSelectionAfter
			})
			.ToList();
	}

	private static ComboRowState ReadComboRowState(object value)
	{
		if (value is ComboRowState state)
		{
			state.Commands ??= new List<ComboCommandConfig>();
			state.AdvanceKey = ComboAdvanceKeys.Normalize(state.AdvanceKey);
			return state;
		}
		return new ComboRowState
		{
			Commands = CloneComboCommands(value as IEnumerable<ComboCommandConfig>),
			AdvanceKey = ComboAdvanceKeys.Default
		};
	}

	private void ApplyGlobalComboAdvanceToRowsAndConfig()
	{
		string key = SelectedGlobalAdvanceKey();
		_config.GlobalComboAdvanceEnabled = _globalComboAdvance.Checked;
		_config.ComboAdvanceKey = key;
		if (!_globalComboAdvance.Checked)
		{
			return;
		}
		foreach (DataGridViewRow row in (IEnumerable)_grid.Rows)
		{
			if (row.IsNewRow || !DisplayToKind(Convert.ToString(row.Cells["Kind"].Value)).Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			ComboRowState state = ReadComboRowState(row.Tag);
			state.AdvanceEnabled = true;
			state.AdvanceKey = key;
			row.Tag = state;
		}
		foreach (MenuItemConfig combo in _config.Menus.Values
			.SelectMany(scene => scene.Rings)
			.SelectMany(ring => ring.Items)
			.Where(item => item.Kind.Equals(ActionKinds.Combo, StringComparison.OrdinalIgnoreCase)))
		{
			combo.ComboAdvanceEnabled = true;
			combo.ComboAdvanceKey = key;
		}
	}

	private string SelectedGlobalAdvanceKey()
	{
		return ComboAdvanceKeys.Normalize(Convert.ToString(_comboAdvanceKey.SelectedItem));
	}

	private void SelectGlobalAdvanceKey(string value)
	{
		string normalized = ComboAdvanceKeys.Normalize(value);
		if (!_comboAdvanceKey.Items.Contains(normalized))
		{
			_comboAdvanceKey.Items.Add(normalized);
		}
		_comboAdvanceKey.SelectedItem = normalized;
	}

	private static string ComboSummary(IEnumerable<ComboCommandConfig> commands)
	{
		List<string> labels = commands
			.Where((ComboCommandConfig command) => command != null && !string.IsNullOrWhiteSpace(command.Value))
			.Select((ComboCommandConfig command) => string.IsNullOrWhiteSpace(command.Label) ? command.Value.Trim() : command.Label.Trim())
			.ToList();
		return labels.Count == 0 ? "" : string.Join(" -> ", labels);
	}

	private static List<KeyboardShortcutRow> MergeShortcutRows(IEnumerable<KeyboardShortcutRow> existing, IEnumerable<KeyboardShortcutRow> incoming)
	{
		Dictionary<string, KeyboardShortcutRow> merged = new Dictionary<string, KeyboardShortcutRow>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyboardShortcutRow row in existing ?? Array.Empty<KeyboardShortcutRow>())
		{
			if (IsQinJianFunctionRow(row) || IsIncomingPluginFunctionRow(row, incoming))
			{
				continue;
			}
			merged[ShortcutRowKey(row)] = row;
		}
		foreach (KeyboardShortcutRow row in incoming ?? Array.Empty<KeyboardShortcutRow>())
		{
			merged[ShortcutRowKey(row)] = row;
		}
		return merged.Values.ToList();
	}

	private static bool IsQinJianFunctionRow(KeyboardShortcutRow row)
	{
		return row != null
			&& (ShortcutActionKind(row).Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
				|| ShortcutActionKind(row).Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
			&& (row.Category ?? "").Trim().StartsWith("琴剑", StringComparison.CurrentCultureIgnoreCase);
	}

	private static bool IsIncomingPluginFunctionRow(KeyboardShortcutRow row, IEnumerable<KeyboardShortcutRow> incoming)
	{
		string kind = ShortcutActionKind(row);
		if (kind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return (incoming ?? Array.Empty<KeyboardShortcutRow>())
			.Any((KeyboardShortcutRow item) => ShortcutActionKind(item).Equals(kind, StringComparison.OrdinalIgnoreCase));
	}

	private static string ShortcutRowKey(KeyboardShortcutRow row)
	{
		return ShortcutActionKind(row) + "|" + (row?.Category ?? "").Trim() + "|" + (row?.Command ?? "").Trim() + "|" + ShortcutActionValue(row);
	}

	private static string ShortcutActionKind(KeyboardShortcutRow row)
	{
		string kind = (row?.ActionKind ?? "").Trim();
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.QinJian;
		}
		if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ExternalPlugin;
		}
		if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.JiaLiChuang;
		}
		if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.NativeCommand;
		}
		return kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase) ? ActionKinds.UiClick : ActionKinds.Hotkey;
	}

	private static string ShortcutActionValue(KeyboardShortcutRow row)
	{
		if (row == null)
		{
			return "";
		}
		string value = (row.Value ?? "").Trim();
		if (value.Length > 0)
		{
			return value;
		}
		return ShortcutActionKind(row).Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)
			? (row.Command ?? "").Trim()
			: (row.Hotkey ?? "").Trim();
	}

	private static string CellKey(int ring, int slot)
	{
		return ring + ":" + slot;
	}

	private void UpdateSelectedCellText()
	{
		Label selectedCellLabel = _selectedCellLabel;
		int? selectedCommandRing = _selectedCommandRing;
		object obj;
		if (selectedCommandRing.HasValue)
		{
			selectedCommandRing = _selectedCommandSlot;
			if (selectedCommandRing.HasValue)
			{
				obj = $"当前填入：第 {_selectedCommandRing.Value} 圈 / 第 {_selectedCommandSlot.Value} 格";
				goto IL_0057;
			}
		}
		obj = "当前填入：未选格子";
		goto IL_0057;
		IL_0057:
		selectedCellLabel.Text = (string)obj;
	}

	private void SaveConfigFromUi()
	{
		try { SaveConfig(); }
		catch (Exception error)
		{
			Logger.Error("Save settings rejected; draft retained", error);
			ToolNotice.Show(this, "设置未保存：" + error.Message + "\r\n请修改后重试，当前编辑内容已保留。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}

	private void SaveConfig()
	{
		ApplyEditorToConfig();
		_commitUserMacroDrafts = true;
		try { PersistEditorConfig(); }
		finally { _commitUserMacroDrafts = false; }
		ToolNotice.Show(this, "已保存。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void PersistEditorConfig()
	{
		PluginConfig latest = ConfigStore.Clone(_load());
		_config.UnknownSettings = latest.UnknownSettings;
		var macroDraft = _config.UserMacros;
		bool saved = false;
		try
		{
		if (_commitUserMacroDrafts) MergeUserMacroSettings(latest);
		else
		{
			// Wheel layout edits save automatically. Tab and macro changes are
			// drafts until the user explicitly applies this settings window.
			_config.UserMacros = latest.UserMacros;
			_config.WheelCommandTabEnabled = latest.WheelCommandTabEnabled;
			_config.WheelCommandTabName = latest.WheelCommandTabName;
		}
		// These settings belong to separate tool windows; only the watermark
		// feature switch is edited here. Imports deliberately replace everything.
		_config.PropertyWatermark = latest.PropertyWatermark ?? new PropertyWatermarkConfig();
		_config.PropertyWatermark.Enabled = _propertyWatermarkEnabled.Checked;

		// The process result windows also edit rules. Keep their latest values
		// wherever this editor has not changed the value it originally displayed.
		ApplyProcessRecognitionControls();
		ProcessRecognitionConfig currentProcess = latest.ProcessRecognition ?? new ProcessRecognitionConfig();
		foreach (var property in typeof(ProcessRecognitionConfig).GetProperties())
		{
			if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
				&& Equals(property.GetValue(_config.ProcessRecognition), property.GetValue(_processEditorBaseline)))
			{
				property.SetValue(_config.ProcessRecognition, property.GetValue(currentProcess));
			}
		}
		// The runtime may keep the saved instance. Later edits must not mutate
		// that committed baseline before the save callback compares changes.
		_save(ConfigStore.Clone(_config));
		saved = true;
		if (_commitUserMacroDrafts) CompleteUserMacroSave();
		LoadProcessRecognitionControls();
		}
		finally
		{
			if (!saved || !_commitUserMacroDrafts) _config.UserMacros = macroDraft;
		}
	}

	private async void ExportConfigBackup()
	{
		try
		{
			using BackupContentDialog contentDialog = new BackupContentDialog(exporting: true);
			if (await contentDialog.ShowModelessAsync(this) != DialogResult.OK)
			{
				return;
			}
			ApplyEditorToConfig();
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Title = "导出XY轮盘备份",
				Filter = "XY轮盘完整备份|*.xyradial|ZIP 备份|*.zip",
				FileName = "XY轮盘备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xyradial",
				OverwritePrompt = true,
				AddExtension = true,
				DefaultExt = "xyradial"
			};
			if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			ShortcutBackupArtifact shortcutArtifact = null;
			if (contentDialog.IncludeShortcuts)
			{
				shortcutArtifact = SolidWorksShortcutBackupService.Export(_getSolidWorks(), ShortcutRowsForBackup());
			}
			if (contentDialog.IncludeConfig)
			{
				PersistEditorConfig();
				_config = _load();
			}
			ConfigBackupService.Export(_config, saveFileDialog.FileName, new ConfigBackupExportOptions
			{
				IncludeConfig = contentDialog.IncludeConfig,
				ShortcutRegistryFile = shortcutArtifact?.RegistryFilePath ?? "",
				ShortcutReportFile = shortcutArtifact?.ReportFilePath ?? ""
			});

			string message = "备份包已导出。";
			if (shortcutArtifact != null)
			{
				message += "\r\n\r\nSW 快捷键：" + shortcutArtifact.ShortcutCount + " 项"
					+ "\r\n独立快捷键文件：\r\n" + shortcutArtifact.RegistryFilePath;
			}
			ToolNotice.Show(this, message, DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		catch (Exception ex)
		{
			Logger.Error("Export config backup failed", ex);
			ToolNotice.Show(this, "导出失败: " + ex.Message, DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private bool CanAssignShortcut(KeyboardShortcutRow shortcut)
	{
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase)
			|| ShortcutActionKind(shortcut).Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase)
			|| ShortcutActionKind(shortcut).Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
			|| ShortcutActionKind(shortcut).Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase)
			|| ShortcutActionKind(shortcut).Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!ShortcutChangeDetector.HasConflict(_shortcutRows, shortcut?.Hotkey, out ShortcutConflictResult conflict))
		{
			return true;
		}
		Logger.Error($"SOLIDWORKS shortcut conflict detected. Hotkey={conflict.Hotkey}; Commands={string.Join(" | ", conflict.Commands)}");
		ToolNotice.Show(this,
			$"快捷键 {conflict.Hotkey} 同时对应以下命令：\r\n{string.Join("\r\n", conflict.Commands)}\r\n\r\n请先在 SOLIDWORKS 自定义键盘中消除冲突。",
			"快捷键冲突",
			MessageBoxButtons.OK,
			MessageBoxIcon.Warning);
		return false;
	}

	private void LoadSelectedCellFontAppearance()
	{
		DataGridViewRow row = FindSelectedCommandRow();
		bool exists = IsOccupiedCommandRow(row);
		int fontSize = exists ? ParseFontSize(Convert.ToString(row.Cells["FontSize"].Value)) : 0;
		string fontName = exists ? NormalizeFontName(Convert.ToString(row.Cells["FontName"].Value)) : "";
		string textColor = exists ? NormalizeColorText(Convert.ToString(row.Cells["TextColor"].Value)) : "";
		_syncingCellAppearance = true;
		try
		{
			_autoFitCellFont.Enabled = exists;
			_autoFitCellFont.Checked = !exists || fontSize <= 0;
			_fontSize.Enabled = exists && fontSize > 0;
			int value = fontSize > 0 ? fontSize : _config.FontSizePx;
			_fontSize.Value = Math.Max(_fontSize.Minimum, Math.Min(_fontSize.Maximum, value));
			_fontName.Enabled = exists;
			SelectFontName(fontName.Length == 0 ? _config.FontName : fontName);
			_cellTextColor.Enabled = exists;
			SetCellColorButton(textColor.Length == 0 ? Color.FromArgb(18, 28, 38) : ParseColorOrDefault(textColor, Color.FromArgb(18, 28, 38)));
		}
		finally
		{
			_syncingCellAppearance = false;
		}
	}

	private void ApplySelectedCellFontSize()
	{
		if (_syncingCellAppearance || _loadingScene)
		{
			return;
		}
		DataGridViewRow row = FindSelectedCommandRow();
		if (row == null || (string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["Value"].Value))
			&& ReadComboRowState(row.Tag).Commands.Count == 0))
		{
			return;
		}
		row.Cells["FontSize"].Value = _autoFitCellFont.Checked ? "" : Convert.ToString((int)_fontSize.Value);
		_fontSize.Enabled = !_autoFitCellFont.Checked;
		RefreshPreview();
	}

	private void ApplySelectedCellFontName()
	{
		if (_syncingCellAppearance || _loadingScene)
		{
			return;
		}
		DataGridViewRow row = FindSelectedCommandRow();
		if (!IsOccupiedCommandRow(row))
		{
			return;
		}
		row.Cells["FontName"].Value = SelectedFontName();
		RefreshPreview();
	}

	private void ApplyFontNameToAllCells()
	{
		string fontName = SelectedFontName();
		foreach (DataGridViewRow row in OccupiedCommandRows())
		{
			row.Cells["FontName"].Value = fontName;
		}
		RefreshPreview();
	}

	private void ApplyAutoFitToAllCells()
	{
		foreach (DataGridViewRow row in OccupiedCommandRows())
		{
			row.Cells["FontSize"].Value = "";
		}
		LoadSelectedCellFontAppearance();
		RefreshPreview();
	}

	private void PickSelectedCellTextColor()
	{
		DataGridViewRow row = FindSelectedCommandRow();
		if (!IsOccupiedCommandRow(row))
		{
			ToolNotice.Show(this, "请先选择已经设置功能的轮盘格。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		using ColorDialog colorDialog = new ColorDialog
		{
			FullOpen = true,
			Color = ParseColorOrDefault(Convert.ToString(row.Cells["TextColor"].Value), Color.FromArgb(18, 28, 38))
		};
		if (colorDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		string color = ColorTranslator.ToHtml(Color.FromArgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B));
		row.Cells["TextColor"].Value = color;
		SetCellColorButton(colorDialog.Color);
		RefreshPreview();
	}

	private void ApplyTextColorToAllCells()
	{
		DataGridViewRow selected = FindSelectedCommandRow();
		if (!IsOccupiedCommandRow(selected))
		{
			return;
		}
		string color = NormalizeColorText(Convert.ToString(selected.Cells["TextColor"].Value));
		if (color.Length == 0)
		{
			color = "#121C26";
		}
		foreach (DataGridViewRow row in OccupiedCommandRows())
		{
			row.Cells["TextColor"].Value = color;
		}
		SetCellColorButton(ParseColorOrDefault(color, Color.FromArgb(18, 28, 38)));
		RefreshPreview();
	}

	private IEnumerable<DataGridViewRow> OccupiedCommandRows()
	{
		return _grid.Rows.Cast<DataGridViewRow>().Where(IsOccupiedCommandRow);
	}

	private static bool IsOccupiedCommandRow(DataGridViewRow row)
	{
		return row != null && !row.IsNewRow
			&& (!string.IsNullOrWhiteSpace(Convert.ToString(row.Cells["Value"].Value))
				|| ReadComboRowState(row.Tag).Commands.Count > 0);
	}

	private void SetCellColorButton(Color color)
	{
		Color opaque = Color.FromArgb(color.R, color.G, color.B);
		_cellTextColor.BackColor = opaque;
		_cellTextColor.ForeColor = IsDark(opaque) ? Color.White : Color.Black;
		_cellTextColor.FlatAppearance.BorderColor = Color.FromArgb(170, 180, 190);
	}

	private void LoadInstalledFontNames()
	{
		SortedSet<string> names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
		try
		{
			using InstalledFontCollection installed = new InstalledFontCollection();
			foreach (FontFamily family in installed.Families)
			{
				names.Add(family.Name);
			}
		}
		catch
		{
		}
		using (Font defaultFont = new Font("SimSun", 9f))
		{
			names.Add(defaultFont.FontFamily.Name);
		}
		foreach (string name in names)
		{
			_fontName.Items.Add(name);
		}
		SelectFontName(_config.FontName);
	}

	private string SelectedFontName()
	{
		string selected = NormalizeFontName(Convert.ToString(_fontName.SelectedItem));
		return selected.Length == 0 ? "SimSun" : selected;
	}

	private void SelectFontName(string value)
	{
		string requested = NormalizeFontName(value);
		if (requested.Length == 0)
		{
			requested = "SimSun";
		}
		foreach (object item in _fontName.Items)
		{
			if (string.Equals(Convert.ToString(item), requested, StringComparison.CurrentCultureIgnoreCase))
			{
				_fontName.SelectedItem = item;
				return;
			}
		}
		try
		{
			using Font font = new Font(requested, 9f);
			string resolved = font.FontFamily.Name;
			foreach (object item in _fontName.Items)
			{
				if (string.Equals(Convert.ToString(item), resolved, StringComparison.CurrentCultureIgnoreCase))
				{
					_fontName.SelectedItem = item;
					return;
				}
			}
			_fontName.Items.Add(resolved);
			_fontName.SelectedItem = resolved;
		}
		catch
		{
			if (_fontName.Items.Count > 0)
			{
				_fontName.SelectedIndex = 0;
			}
		}
	}

	private async void ImportConfigBackup()
	{
		try
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "导入XY轮盘备份",
				Filter = "XY轮盘备份/快捷键|*.xyradial;*.zip;*.json;*.sldreg|所有文件|*.*",
				CheckFileExists = true
			};
			if (openFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			ConfigBackupContents contents = ConfigBackupService.Inspect(openFileDialog.FileName);
			using BackupContentDialog contentDialog = new BackupContentDialog(
				exporting: false,
				configAvailable: contents.HasConfig,
				shortcutsAvailable: contents.HasShortcuts);
			if (await contentDialog.ShowModelessAsync(this) != DialogResult.OK)
			{
				return;
			}
			if (contentDialog.IncludeConfig
				&& MessageBox.Show(this, "导入轮盘配置会覆盖当前轮盘设置，是否继续？", DisplayName(), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
			{
				return;
			}

			if (contentDialog.IncludeConfig)
			{
				PluginConfig pluginConfig = ConfigBackupService.Import(openFileDialog.FileName, ConfigStore.AssetDirectory);
				_save(pluginConfig);
				CleanupPendingUserMacroImports();
				_removedUserMacros.Clear();
				_config = _load();
				LoadConfigIntoControls();
			}

			if (contentDialog.IncludeShortcuts)
			{
				ExtractedShortcutBackup extracted = ConfigBackupService.ExtractShortcuts(openFileDialog.FileName, SolidWorksShortcutBackupService.BackupDirectory);
				ShortcutImportResult result = SolidWorksShortcutBackupService.ImportDirect(
					extracted.RegistryFilePath,
					extracted.ReportFilePath,
					_getSolidWorks(),
					ShortcutRowsForBackup());
				string text = contentDialog.IncludeConfig ? "轮盘配置已导入。\r\n\r\n" + result.ReportText : result.ReportText;
				using TextResultDialog dialog = new TextResultDialog("SOLIDWORKS 快捷键导入结果", text, result.ReportPath);
				await dialog.ShowModelessAsync(this);
			}
			else
			{
				ToolNotice.Show(this, "轮盘配置已导入。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
		catch (Exception ex)
		{
			Logger.Error("Import config backup failed", ex);
			ToolNotice.Show(this, "导入失败: " + ex.Message, DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private List<KeyboardShortcutRow> ShortcutRowsForBackup()
	{
		List<KeyboardShortcutRow> rows = _shortcutRows
			.Where(row => row != null
				&& !string.IsNullOrWhiteSpace(row.Hotkey)
				&& !row.Hotkey.Equals("直接执行", StringComparison.OrdinalIgnoreCase)
				&& (string.IsNullOrWhiteSpace(row.ActionKind) || row.ActionKind.Equals(ActionKinds.Hotkey, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (rows.Count > 0)
		{
			return rows;
		}
		if (KeyboardListReader.TryRead(out List<KeyboardShortcutRow> detected, out _))
		{
			SolidWorksNativeCommandCatalog.ClassifyShortcutRows(_getSolidWorks(), detected);
			return detected;
		}
		return rows;
	}

	private void ApplyEditorToConfig()
	{
		EndPreviewEditing();
		ApplyRows();
		_config.TriggerMode = SelectedTriggerMode();
		_config.HoldDelayMs = (int)_hold.Value;
		_config.OpacityPercent = _menuOpacity.Value;
		if (_mode == SettingsWindowMode.Wheel)
		{
			foreach (var entry in _sceneSwitches) GetScene(entry.Key).Enabled = entry.Value.Checked;
			_config.SketchWheelEnabled = _sceneSwitches[SceneKeys.Sketch].Checked;
		}
		_config.GlobalComboAdvanceEnabled = _globalComboAdvance.Checked;
		_config.RadialMenuEnabled = _radialMenuEnabled.Checked;
		_config.ShortcutPaletteEnabled = _shortcutPaletteEnabled.Checked;
		_config.ExplodeEnabled = _explodeEnabled.Checked;
		ApplyCommandTabSettings();
		_config.PropertyWatermark ??= new PropertyWatermarkConfig();
		_config.PropertyWatermark.Enabled = _propertyWatermarkEnabled.Checked;
		// Preserve retired quotation settings for compatibility.
		_config.ComboAdvanceKey = SelectedGlobalAdvanceKey();
		ApplyProcessRecognitionControls();
	}

	private void LoadConfigIntoControls()
	{
		_loadingScene = true;
		SelectTriggerMode(_config.TriggerMode);
		_hold.Value = Math.Max(_hold.Minimum, Math.Min(_hold.Maximum, _config.HoldDelayMs));
		_menuOpacity.Value = Math.Max(_menuOpacity.Minimum, Math.Min(_menuOpacity.Maximum, _config.OpacityPercent));
		_menuSize.Value = Math.Max(_menuSize.Minimum, Math.Min(_menuSize.Maximum, _config.MenuSizePx));
		_centerRadius.Value = Math.Max(_centerRadius.Minimum, Math.Min(_centerRadius.Maximum, _config.CenterRadiusPx));
		_fontSize.Value = Math.Max(_fontSize.Minimum, Math.Min(_fontSize.Maximum, _config.FontSizePx));
		SelectFontName(_config.FontName);
		_radialMenuEnabled.Checked = _config.RadialMenuEnabled;
		_shortcutPaletteEnabled.Checked = _config.ShortcutPaletteEnabled;
		_explodeEnabled.Checked = _config.ExplodeEnabled;
		LoadUserMacroSettings();
		_propertyWatermarkEnabled.Checked = _config.PropertyWatermark?.Enabled == true;
		SelectGlobalAdvanceKey(_config.ComboAdvanceKey);
		_globalComboAdvance.Checked = _config.GlobalComboAdvanceEnabled;
		LoadProcessRecognitionControls();
		UpdateFeatureSwitchDependencies();
		_loadingScene = false;
		if (_scene.SelectedIndex < 0)
		{
			_scene.SelectedIndex = 0;
		}
		else
		{
			LoadSceneRows();
		}
		RenderSection(_activeSection);
		RefreshPreview();
	}

	private void LoadSceneSwitches()
	{
		foreach (var entry in _sceneSwitches)
			entry.Value.Checked = GetScene(entry.Key).Enabled ?? (entry.Key != SceneKeys.Sketch || _config.SketchWheelEnabled);
	}

	private void LoadProcessRecognitionControls()
	{
		_config.ProcessRecognition ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig.Normalize(_config.ProcessRecognition);
		ProcessRecognitionConfig config = _config.ProcessRecognition;
		_processEnabled.Checked = config.Enabled;
		SelectSettingChoice(_processDefaultMode, config.DefaultMode);
		_processSummaryProperty.Text = config.SummaryPropertyName;
		_processSeparator.Text = config.Separator;
		SelectSettingChoice(_processSurfaceMode, config.SurfaceMode);
		_processSurfaceProperty.Text = config.SurfacePropertyName;
		_processSurfaceFallback.Text = config.DefaultSurfaceProcess;
		_processSurfaceRules.Text = config.SurfaceMaterialRules;
		_processCraftProperty.Text = config.CraftDescriptionPropertyName;
		_processThicknessProperty.Text = config.ThicknessPropertyName;
		_processBendCountProperty.Text = config.BendCountPropertyName;
		_processCutoutCountProperty.Text = config.CutoutCountPropertyName;
		_processPartTypeProperty.Text = config.PartTypePropertyName;
		_processProcessTypeProperty.Text = config.ProcessTypePropertyName;
		_processRemarkProperty.Text = config.RemarkPropertyName;
		_processOutsourceProperty.Text = config.OutsourcePropertyName;
		_processDeburrName.Text = config.DeburrProcessName;
		_processInspectionName.Text = config.InspectionProcessName;
		_processPackagingName.Text = config.PackagingProcessName;
		_processShippingName.Text = config.ShippingProcessName;
		_processAdditionalProcesses.Text = config.AdditionalProcesses;
		_processNameMappings.Text = config.ProcessNameMappings;
		_processCombinations.Text = config.ProcessCombinations;
		_processTypeGroups = config.ProcessTypeGroups;
		_processCategories.Text = config.ProcessCategories;
		_processAssemblyBatchSize.Value = Math.Max(_processAssemblyBatchSize.Minimum, Math.Min(_processAssemblyBatchSize.Maximum, config.AssemblyBatchSize));
		_processAssemblyBatchDelay.Value = Math.Max(_processAssemblyBatchDelay.Minimum, Math.Min(_processAssemblyBatchDelay.Maximum, config.AssemblyBatchDelayMs));
		_processWatermark.Checked = config.SyncWatermark;
		_processCraftDescription.Checked = config.WriteCraftDescription;
		_processGeometryProperties.Checked = config.WriteGeometryProperties;
		_processAssemblyAuto.Checked = config.Enabled;
		_processDeburr.Checked = config.IncludeDeburr;
		_processInspection.Checked = config.IncludeInspection;
		_processInspectionAfterSurface.Checked = config.InspectionAfterSurface;
		_processPackaging.Checked = config.IncludePackaging;
		_processShipping.Checked = config.IncludeShipping;
		_processEditorBaseline = ProcessRulesWindow.Copy(config);
		UpdateProcessRecognitionControlState();
	}

	private void ApplyProcessRecognitionControls()
	{
		_config.ProcessRecognition ??= new ProcessRecognitionConfig();
		ProcessRecognitionConfig config = _config.ProcessRecognition;
		config.Enabled = _processEnabled.Checked;
		config.DefaultMode = SelectedSettingKey(_processDefaultMode, ProcessRecognitionModes.Deep);
		config.SummaryPropertyName = _processSummaryProperty.Text;
		config.Separator = _processSeparator.Text;
		config.SurfaceMode = SelectedSettingKey(_processSurfaceMode, SurfaceRecognitionModes.Material);
		config.SurfacePropertyName = _processSurfaceProperty.Text;
		config.DefaultSurfaceProcess = _processSurfaceFallback.Text;
		config.SurfaceMaterialRules = _processSurfaceRules.Text;
		config.CraftDescriptionPropertyName = _processCraftProperty.Text;
		config.ThicknessPropertyName = _processThicknessProperty.Text;
		config.BendCountPropertyName = _processBendCountProperty.Text;
		config.CutoutCountPropertyName = _processCutoutCountProperty.Text;
		config.PartTypePropertyName = _processPartTypeProperty.Text;
		config.ProcessTypePropertyName = _processProcessTypeProperty.Text;
		config.RemarkPropertyName = _processRemarkProperty.Text;
		config.OutsourcePropertyName = _processOutsourceProperty.Text;
		config.DeburrProcessName = _processDeburrName.Text;
		config.InspectionProcessName = _processInspectionName.Text;
		config.PackagingProcessName = _processPackagingName.Text;
		config.ShippingProcessName = _processShippingName.Text;
		config.AdditionalProcesses = _processAdditionalProcesses.Text;
		config.ProcessNameMappings = _processNameMappings.Text;
		config.ProcessCombinations = _processCombinations.Text;
		config.ProcessTypeGroups = _processTypeGroups;
		config.ProcessCategories = _processCategories.Text;
		config.AssemblyBatchSize = (int)_processAssemblyBatchSize.Value;
		config.AssemblyBatchDelayMs = (int)_processAssemblyBatchDelay.Value;
		config.SyncWatermark = _processWatermark.Checked;
		config.WriteCraftDescription = _processCraftDescription.Checked;
		config.WriteGeometryProperties = _processGeometryProperties.Checked;
		// Legacy field retained for older versions; background scheduling follows Enabled.
		config.IncludeDeburr = _processDeburr.Checked;
		config.IncludeInspection = _processInspection.Checked;
		config.InspectionAfterSurface = _processInspectionAfterSurface.Checked;
		config.IncludePackaging = _processPackaging.Checked;
		config.IncludeShipping = _processShipping.Checked;
		ProcessRecognitionConfig.Normalize(config);
	}

	private void UpdateProcessRecognitionControlState()
	{
		bool enabled = _processEnabled.Checked;
		Control[] controls =
		{
			_processDefaultMode, _processSummaryProperty, _processSeparator,
			_processSurfaceMode, _processSurfaceProperty, _processSurfaceFallback, _processSurfaceRules,
			_processCraftProperty, _processThicknessProperty, _processBendCountProperty, _processCutoutCountProperty,
			_processPartTypeProperty, _processProcessTypeProperty, _processRemarkProperty, _processOutsourceProperty,
			_processDeburrName, _processInspectionName, _processPackagingName, _processShippingName,
			_processAdditionalProcesses, _processNameMappings, _processCombinations, _processCategories,
			_processAssemblyBatchSize, _processAssemblyBatchDelay
		};
		foreach (Control control in controls) control.Enabled = enabled;
		UpdateFeatureSwitchDependencies();
	}

	private void UpdateFeatureSwitchDependencies()
	{
		bool processEnabled = _processEnabled.Checked;
		_processAssemblyAuto.Enabled = false;
		_processAssemblyAuto.Checked = processEnabled;
		_processWatermark.Enabled = processEnabled && _propertyWatermarkEnabled.Checked;
		_processCraftDescription.Enabled = processEnabled;
		_processGeometryProperties.Enabled = processEnabled;
		_processDeburr.Enabled = processEnabled;
		_processInspection.Enabled = processEnabled;
		_processInspectionAfterSurface.Enabled = processEnabled && _processInspection.Checked;
		_processPackaging.Enabled = processEnabled;
		_processShipping.Enabled = processEnabled;

		bool radialEnabled = _radialMenuEnabled.Checked;
		foreach (var entry in _sceneSwitches) entry.Value.Enabled = radialEnabled;
		_globalComboAdvance.Enabled = radialEnabled;
		_comboAdvanceKey.Enabled = radialEnabled && _globalComboAdvance.Checked;

	}

	private static void SelectSettingChoice(ComboBox comboBox, string key)
	{
		foreach (object item in comboBox.Items)
		{
			if (item is SettingChoice choice && string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				comboBox.SelectedItem = item;
				return;
			}
		}
		if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
	}

	private static string SelectedSettingKey(ComboBox comboBox, string fallback)
	{
		return comboBox.SelectedItem is SettingChoice choice ? choice.Key : fallback;
	}

	private void RingCountChanged()
	{
		if (!_syncingRingInputs)
		{
			RebuildRingEditor();
			QueueRingDefinitionSync();
		}
	}

	private void RingDefinitionValueChanged()
	{
		if (!_syncingRingInputs)
		{
			QueueRingDefinitionSync();
		}
	}

	private void QueueRingDefinitionSync()
	{
		_ringSyncTimer.Stop();
		_ringSyncTimer.Start();
	}

	private void RebuildRingEditor(List<RingConfig>? source = null)
	{
		if (_syncingRingInputs && source == null)
		{
			return;
		}
		List<RingConfig> list = source?.OrderBy((RingConfig r) => r.Index).ToList() ?? _ringSlotInputs.Select((NumericUpDown input, int index) => new RingConfig
		{
			Index = index,
			Slots = (int)input.Value,
			SizeWeight = 1,
			DiameterPx = index >= _ringDiameterInputs.Count ? 0 : (int)_ringDiameterInputs[index].Value,
			Enabled = true
		}).ToList();
		if (list.Count == 0)
		{
			list.Add(new RingConfig
			{
				Index = 0,
				Slots = 8,
				SizeWeight = 1,
				Enabled = true
			});
		}
		int num = Math.Max(1, Math.Min(4, (int)_ringCount.Value));
		int previousCount = list.Count;
		while (list.Count < num)
		{
			RingConfig ringConfig = list[list.Count - 1];
			list.Add(new RingConfig
			{
				Index = list.Count,
				Slots = ringConfig.Slots,
				SizeWeight = 1,
				Enabled = true
			});
		}
		list = list.Take(num).ToList();
		for (int index = 0; index < list.Count; index++)
		{
			list[index].Index = index;
			list[index].Enabled = true;
		}
		SceneMenuConfig layout = new SceneMenuConfig
		{
			MenuSizePx = (int)_menuSize.Value,
			CenterRadiusPx = Math.Max(20, Math.Min((int)_menuSize.Value / 3, (int)_centerRadius.Value)),
			EqualRingBands = _equalRingBands.Checked,
			StyleId = GetScene(_activeSceneKey)?.StyleId ?? "01",
			Rings = list
		};
		if (num > previousCount)
		{
			RingDiameterLayout.SplitOutermostRing(layout, previousCount);
		}
		else
		{
			RingDiameterLayout.Normalize(layout);
		}

		bool previousSync = _syncingRingInputs;
		_syncingRingInputs = true;
		_ringEditor.SuspendLayout();
		try
		{
			_ringEditor.Controls.Clear();
			_ringSlotInputs.Clear();
			_ringDiameterInputs.Clear();
			int centerDiameter = layout.CenterRadiusPx * 2;
			int step = Math.Max(1, Math.Min(RingDiameterLayout.MinimumDiameterStepPx,
				Math.Max(num, layout.MenuSizePx - centerDiameter) / num));
			for (int num2 = 0; num2 < num; num2++)
			{
				RingConfig ringConfig2 = list[num2];
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				WrapContents = false,
				Margin = new Padding(0, 2, 0, 2)
			};
			flowLayoutPanel.Controls.Add(new Label
			{
				Text = $"第 {num2 + 1} 圈",
				Width = 58,
				Padding = new Padding(0, 6, 0, 0)
			});
			flowLayoutPanel.Controls.Add(new Label
			{
				Text = "格数",
				AutoSize = true,
				Padding = new Padding(0, 6, 4, 0)
			});
			NumericUpDown numericUpDown = new NumericUpDown
			{
				Minimum = 1m,
				Maximum = 32m,
				Value = Math.Max(1, Math.Min(32, ringConfig2.Slots)),
				Width = 64
			};
			flowLayoutPanel.Controls.Add(numericUpDown);
			flowLayoutPanel.Controls.Add(new Label
			{
				Text = "直径",
				AutoSize = true,
				Padding = new Padding(16, 6, 4, 0)
			});
			int minimum = centerDiameter + step * (num2 + 1);
			int maximum = num2 == num - 1 ? 900 : layout.MenuSizePx - step * (num - num2 - 1);
			NumericUpDown numericUpDown2 = new NumericUpDown
			{
				Minimum = Math.Min(minimum, maximum),
				Maximum = Math.Max(minimum, maximum),
				Increment = 10m,
				Value = Math.Max(Math.Min(minimum, maximum), Math.Min(Math.Max(minimum, maximum), ringConfig2.DiameterPx)),
				Width = 72,
				// Equal bands put the diameter under the layout's control, so
				// leaving the box editable would only fight the recompute.
				Enabled = !_equalRingBands.Checked
			};
			flowLayoutPanel.Controls.Add(numericUpDown2);
			numericUpDown.ValueChanged += delegate
			{
				RingDefinitionValueChanged();
			};
			int captured = num2;
			numericUpDown2.ValueChanged += delegate
			{
				if (_syncingRingInputs)
				{
					return;
				}
				if (captured == (int)_ringCount.Value - 1)
				{
					_syncingRingInputs = true;
					_menuSize.Value = Math.Max(_menuSize.Minimum, Math.Min(_menuSize.Maximum, numericUpDown2.Value));
					_syncingRingInputs = false;
				}
				QueueRingDefinitionSync();
				RefreshPreview();
			};
			_ringSlotInputs.Add(numericUpDown);
			_ringDiameterInputs.Add(numericUpDown2);
			_ringEditor.Controls.Add(flowLayoutPanel);
			}
		}
		finally
		{
			_ringEditor.ResumeLayout(true);
			_syncingRingInputs = previousSync;
		}
	}

	private void RefreshPreview()
	{
		if (_loadingScene || _refreshingPreview || string.IsNullOrWhiteSpace(_activeSceneKey))
		{
			return;
		}
		try
		{
			_refreshingPreview = true;
			ApplyRows(_activeSceneKey);
			_config.TriggerMode = SelectedTriggerMode();
			_config.HoldDelayMs = (int)_hold.Value;
			_config.OpacityPercent = _menuOpacity.Value;
			_preview.SetMenu(_config, GetScene(_activeSceneKey));
		}
		catch (Exception ex)
		{
			Logger.Error("Settings preview refresh failed", ex);
		}
		finally
		{
			_refreshingPreview = false;
		}
	}

	private void RefreshShortcutRows()
	{
		if (_shortcutGrid.Columns.Count == 0)
		{
			return;
		}
		string keyword = _shortcutSearch.Text.Trim();
		string category = Convert.ToString(_commandCategoryTree.SelectedNode?.Tag) ?? "";
		_shortcutGrid.Rows.Clear();
		foreach (KeyboardShortcutRow item in _shortcutRows.Where(row => CategoryMatches(row, category)).Where(row => CommandMatchesSearch(row, keyword)))
		{
			string alias = PreferredCommandAlias(item);
			int index = _shortcutGrid.Rows.Add(CatalogIconBitmap(item), item.Hotkey, string.IsNullOrWhiteSpace(alias) ? item.Command : alias, DisplayCategory(item));
			_shortcutGrid.Rows[index].Tag = item;
		}
	}

	private bool CommandMatchesSearch(KeyboardShortcutRow row, string keyword)
	{
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return true;
		}
		string alias = PreferredCommandAlias(row);
		return (row?.Command ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
			|| (row?.Hotkey ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
			|| (row?.Category ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
			|| alias.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
			|| DisplayCategory(row).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
			|| (ShortcutActionKind(row).Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)
				&& "扩展功能".IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private string PreferredCommandAlias(KeyboardShortcutRow command)
	{
		if (command == null)
		{
			return "";
		}
		string kind = ShortcutActionKind(command);
		string value = ShortcutActionValue(command);
		IEnumerable<SceneMenuConfig> scenes = _config.Menus
			.OrderByDescending(pair => pair.Key.Equals(_activeSceneKey, StringComparison.OrdinalIgnoreCase))
			.Select(pair => pair.Value);
		MenuItemConfig match = scenes
			.Where(scene => scene?.Rings != null)
			.SelectMany(scene => scene.Rings)
			.SelectMany(ring => ring.Items ?? new List<MenuItemConfig>())
			.FirstOrDefault(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(item.Value?.Trim(), value, StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrWhiteSpace(item.Label));
		return match?.Label?.Trim() ?? "";
	}

	private void RefreshCommandCategoryTree()
	{
		string selected = Convert.ToString(_commandCategoryTree.SelectedNode?.Tag) ?? "";
		_commandCategoryTree.BeginUpdate();
		_commandCategoryTree.Nodes.Clear();
		TreeNode root = new TreeNode("全部命令") { Tag = "" };
		_commandCategoryTree.Nodes.Add(root);
		foreach (string category in _shortcutRows.Select(DisplayCategory).Where(value => value.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
		{
			TreeNode parent = root;
			string path = "";
			foreach (string segment in category.Split('>').Select(value => value.Trim()).Where(value => value.Length > 0))
			{
				path = path.Length == 0 ? segment : path + " > " + segment;
				TreeNode node = parent.Nodes.Cast<TreeNode>().FirstOrDefault(item => item.Text.Equals(segment, StringComparison.CurrentCultureIgnoreCase));
				if (node == null)
				{
					node = new TreeNode(segment) { Tag = path };
					parent.Nodes.Add(node);
				}
				parent = node;
			}
		}
		root.Expand();
		TreeNode selectedNode = FindCategoryNode(root, selected) ?? root;
		_commandCategoryTree.SelectedNode = selectedNode;
		if (FindCategoryNode(root, "SOLIDWORKS") is TreeNode solidWorks)
		{
			solidWorks.Expand();
		}
		_commandCategoryTree.EndUpdate();
	}

	private void SelectCommandCategory(string category)
	{
		TreeNode root = _commandCategoryTree.Nodes.Count > 0 ? _commandCategoryTree.Nodes[0] : null;
		TreeNode node = root == null ? null : FindCategoryNode(root, category);
		if (node != null)
		{
			_commandCategoryTree.SelectedNode = node;
			node.EnsureVisible();
		}
		RefreshShortcutRows();
	}

	private static TreeNode FindCategoryNode(TreeNode node, string category)
	{
		if (string.Equals(Convert.ToString(node.Tag), category ?? "", StringComparison.CurrentCultureIgnoreCase))
		{
			return node;
		}
		foreach (TreeNode child in node.Nodes)
		{
			TreeNode match = FindCategoryNode(child, category);
			if (match != null)
			{
				return match;
			}
		}
		return null;
	}

	private static bool CategoryMatches(KeyboardShortcutRow row, string selectedCategory)
	{
		if (string.IsNullOrWhiteSpace(selectedCategory))
		{
			return true;
		}
		string category = DisplayCategory(row);
		return category.Equals(selectedCategory, StringComparison.CurrentCultureIgnoreCase)
			|| category.StartsWith(selectedCategory + " > ", StringComparison.CurrentCultureIgnoreCase);
	}

	private static string DisplayCategory(KeyboardShortcutRow row)
	{
		string kind = ShortcutActionKind(row);
		string raw = (row?.Category ?? "").Trim();
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)
			|| kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase)
			|| raw.IndexOf("琴剑", StringComparison.CurrentCultureIgnoreCase) >= 0
			|| raw.IndexOf("嘉立创", StringComparison.CurrentCultureIgnoreCase) >= 0
			|| raw.IndexOf("博士", StringComparison.CurrentCultureIgnoreCase) >= 0
			|| raw.IndexOf("第三方插件", StringComparison.CurrentCultureIgnoreCase) >= 0)
		{
			// Keep the vendor/tab path under one stable root.  Collapsing every
			// external command to the bare "外部功能" node made the category tree
			// look empty/incorrect and made similarly named commands impossible to
			// distinguish after reading more than one plug-in.
			string path = raw;
			if (path.StartsWith("第三方插件 > ", StringComparison.CurrentCultureIgnoreCase))
				path = path.Substring("第三方插件 > ".Length).Trim();
			if (path.StartsWith("外部功能 > ", StringComparison.CurrentCultureIgnoreCase))
				path = path.Substring("外部功能 > ".Length).Trim();
			return path.Length == 0 ? "外部功能" : "外部功能 > " + path;
		}
		return (row?.Category ?? "").Trim();
	}

	private static string SanitizeExternalMessage(string message)
		=> (message ?? "").Replace("琴剑", "外部功能").Replace("嘉立创", "扩展功能").Replace("启翔铝型材", "外部型材功能").Replace("博士", "外部工具").Replace("Doctor", "外部工具").Replace("doctor", "外部工具");

	private void SetRingInputsFromScene(SceneMenuConfig scene)
	{
		RingDiameterLayout.Normalize(scene);
		List<RingConfig> list = scene.Rings.Where(r => r.Enabled).OrderBy((RingConfig r) => r.Index).ToList();
		if (list.Count == 0)
		{
			list.Add(new RingConfig
			{
				Index = 0,
				Slots = 8
			});
		}
		_syncingRingInputs = true;
		_equalRingBands.Checked = scene.EqualRingBands;
		_ringCount.Value = Math.Max(_ringCount.Minimum, Math.Min(_ringCount.Maximum, list.Count));
		RebuildRingEditor(list);
		_syncingRingInputs = false;
	}

	private void SetAppearanceInputsFromScene(SceneMenuConfig scene)
	{
		int menuSize = scene.MenuSizePx > 0 ? scene.MenuSizePx : _config.MenuSizePx;
		int centerRadius = scene.CenterRadiusPx > 0 ? scene.CenterRadiusPx : _config.CenterRadiusPx;
		_menuSize.Value = Math.Max(_menuSize.Minimum, Math.Min(_menuSize.Maximum, menuSize));
		int maxCenter = Math.Max((int)_centerRadius.Minimum, Math.Min((int)_centerRadius.Maximum, ((int)_menuSize.Value) / 3));
		_centerRadius.Value = Math.Max(_centerRadius.Minimum, Math.Min(maxCenter, centerRadius));
	}

	private List<RingDefinition> GetRingDefinitionsFromInputs()
	{
		int num = Math.Max(1, Math.Min(4, (int)_ringCount.Value));
		List<RingDefinition> list = new List<RingDefinition>();
		for (int i = 0; i < num; i++)
		{
			int val = ((i < _ringSlotInputs.Count) ? ((int)_ringSlotInputs[i].Value) : 8);
			int diameter = i >= _ringDiameterInputs.Count ? 0 : (int)_ringDiameterInputs[i].Value;
			list.Add(new RingDefinition(i, Math.Max(1, Math.Min(32, val)), 1, Math.Max(0, diameter)));
		}
		return list;
	}

	private string SelectedTriggerMode()
	{
		if (!(_triggerMode.SelectedItem is TriggerChoice triggerChoice))
		{
			return "right-hold";
		}
		return triggerChoice.Key;
	}

	private void SelectTriggerMode(string mode)
	{
		string text = TriggerModes.Normalize(mode);
		foreach (TriggerChoice item in _triggerMode.Items)
		{
			if (item.Key == text)
			{
				_triggerMode.SelectedItem = item;
				return;
			}
		}
		if (_triggerMode.Items.Count > 0)
		{
			_triggerMode.SelectedIndex = 0;
		}
	}

	private static Button MakeButton(string text, Action action, int width = 86, bool primary = false)
	{
		Button button = new Button();
		button.Text = text;
		button.Width = width;
		button.Height = 28;
		button.FlatStyle = FlatStyle.Flat;
		button.BackColor = (primary ? Color.FromArgb(65, 139, 217) : Color.White);
		button.ForeColor = (primary ? Color.White : Color.FromArgb(35, 45, 55));
		button.Margin = new Padding(4, 2, 4, 2);
		button.FlatAppearance.BorderColor = (primary ? Color.FromArgb(65, 139, 217) : Color.FromArgb(210, 216, 222));
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	private static FlowLayoutPanel ToolbarRow(FlowDirection direction = FlowDirection.LeftToRight)
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = direction,
			WrapContents = false,
			Margin = Padding.Empty,
			BackColor = Color.FromArgb(248, 250, 252)
		};
	}

	private static void StyleGrid(DataGridView grid)
	{
		grid.BackgroundColor = Color.White;
		grid.BorderStyle = BorderStyle.None;
		grid.EnableHeadersVisualStyles = false;
		grid.GridColor = Color.FromArgb(226, 231, 236);
		grid.ColumnHeadersHeight = 34;
		grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 246);
		grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(45, 57, 69);
		grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
		grid.RowTemplate.Height = 30;
		grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(217, 234, 251);
		grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 44, 58);
		grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 253);
	}

	private static void StyleInteractiveControls(Control root)
	{
		foreach (Control control in root.Controls)
		{
			if (control is TextBox textBox)
			{
				textBox.BorderStyle = BorderStyle.FixedSingle;
				textBox.BackColor = Color.White;
			}
			else if (control is ComboBox comboBox)
			{
				comboBox.FlatStyle = FlatStyle.Flat;
				comboBox.BackColor = Color.White;
			}
			else if (control is NumericUpDown numeric)
			{
				numeric.BorderStyle = BorderStyle.FixedSingle;
				numeric.BackColor = Color.White;
			}
			else if (control is TreeView tree)
			{
				tree.ItemHeight = 24;
				tree.Indent = 18;
			}
			StyleInteractiveControls(control);
		}
	}

	private static string DisplayName()
	{
		return "XY轮盘";
	}
}
