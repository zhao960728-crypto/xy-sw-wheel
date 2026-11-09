using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

internal sealed class ComboCommandEditor : PluginToolWindow
{
	private const int MinimumSlots = 1;

	private readonly TextBox _comboName = new TextBox
	{
		Width = 240
	};

	private readonly FlowLayoutPanel _slotFlow = new FlowLayoutPanel
	{
		Dock = DockStyle.Fill,
		WrapContents = false,
		AutoScroll = true,
		BackColor = Color.White
	};

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

	private readonly TextBox _search = new TextBox
	{
		Width = 220
	};
	private readonly Label _searchHint = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(96, 104, 112), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
		Text = "输入功能名按 Enter 衔接；同名功能请从列表选择来源" };

	private readonly CheckBox _advanceEnabled = new CheckBox
	{
		Text = "衔接键",
		AutoSize = true,
		Margin = new Padding(4, 9, 10, 0)
	};

	private readonly ComboBox _advanceKey = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 92,
		Margin = new Padding(4, 5, 12, 0)
	};

	private readonly Func<IntPtr> _getSolidWorksWindowHandle;
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly Func<ICommandManager> _getCommandManager;
	private readonly Func<KeyboardShortcutRow, string> _getDisplayName;

	private readonly TreeView _commandCategoryTree = new TreeView
	{
		Dock = DockStyle.Fill,
		BorderStyle = BorderStyle.None,
		HideSelection = false,
		ShowLines = true,
		ShowPlusMinus = true,
		ShowRootLines = true,
		BackColor = Color.White,
		ItemHeight = 24,
		Indent = 18
	};

	private string _categoryFingerprint = "";

	private readonly List<ComboSlotControl> _slots = new List<ComboSlotControl>();
	private CommandRecorderWindow _recorder;
	private List<KeyboardShortcutRow> _shortcutRows;
	private ComboSlotControl? _activeSlot;

	public string ComboName { get; private set; } = "";

	public List<ComboCommandConfig> Commands { get; private set; } = new List<ComboCommandConfig>();

	public bool ComboAdvanceEnabled { get; private set; }

	public string ComboAdvanceKey { get; private set; } = ComboAdvanceKeys.Default;

	public IReadOnlyList<KeyboardShortcutRow> ShortcutRows => _shortcutRows;

	public ComboCommandEditor(string comboName, IEnumerable<ComboCommandConfig> commands, IEnumerable<KeyboardShortcutRow> shortcutRows, Func<IntPtr>? getSolidWorksWindowHandle = null, bool comboAdvanceEnabled = false, string comboAdvanceKey = "Ctrl", bool globalComboAdvanceEnabled = false, Func<ISldWorks>? getSolidWorks = null, Func<ICommandManager>? getCommandManager = null, Func<KeyboardShortcutRow, string>? getDisplayName = null)
	{
		_getSolidWorksWindowHandle = getSolidWorksWindowHandle ?? (() => IntPtr.Zero);
		_getSolidWorks = getSolidWorks ?? (() => null);
		_getCommandManager = getCommandManager ?? (() => null);
		_getDisplayName = getDisplayName ?? (row => row?.Command ?? "");
		_shortcutRows = (shortcutRows ?? Enumerable.Empty<KeyboardShortcutRow>()).Concat(FunctionNameResolver.BasicRows())
			.Where(ShortcutPaletteMatcher.IsNameCandidate).GroupBy(ShortcutPaletteMatcher.CommandIdentity, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First()).ToList();
		_comboName.Text = comboName ?? "";
		Text = "组合功能";
		Width = 1180;
		Height = 760;
		MinimumSize = new Size(980, 620);
		StartPosition = FormStartPosition.CenterParent;
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
		foreach (string key in ComboAdvanceKeys.Choices)
		{
			_advanceKey.Items.Add(key);
		}
		SelectAdvanceKey(comboAdvanceKey);
		_advanceEnabled.Checked = comboAdvanceEnabled || globalComboAdvanceEnabled;
		_advanceEnabled.Enabled = !globalComboAdvanceEnabled;
		_advanceKey.Enabled = !globalComboAdvanceEnabled;
		ToolTip advanceTip = new ToolTip();
		advanceTip.SetToolTip(_advanceEnabled, globalComboAdvanceEnabled ? "当前由主界面的全局组合衔接控制" : "等待组合命令时，人工按下设置键可直接进入下一步");
		advanceTip.SetToolTip(_advanceKey, "默认 Ctrl；仅在 SOLIDWORKS 窗口位于前台时生效");
		BuildShortcutGrid();
		Controls.Add(BuildShell());
		StyleInteractiveControls(this);
		_commandCategoryTree.AfterSelect += delegate
		{
			RefreshShortcutRows();
		};
		foreach (ComboCommandConfig command in commands ?? Array.Empty<ComboCommandConfig>())
		{
			AddSlot(command);
		}
		while (_slots.Count < MinimumSlots)
		{
			AddSlot(null);
		}
		RenderSlots();
		SetActiveSlot(_slots.FirstOrDefault());
		RefreshShortcutRows();
	}

	private Control BuildShell()
	{
		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 6,
			Padding = new Padding(14),
			BackColor = Color.White
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 184f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		layout.Controls.Add(BuildNameRow(), 0, 0);
		layout.Controls.Add(BuildSlotPane(), 0, 1);
		layout.Controls.Add(BuildAdvanceRow(), 0, 2);
		layout.Controls.Add(BuildShortcutToolbar(), 0, 3);
		layout.Controls.Add(BuildCommandCatalogPane(), 0, 4);
		layout.Controls.Add(BuildActionBar(), 0, 5);
		return layout;
	}

	private Control BuildCommandCatalogPane()
	{
		SplitContainer split = new SplitContainer
		{
			Dock = DockStyle.Fill,
			FixedPanel = FixedPanel.Panel1,
			SplitterDistance = 230,
			SplitterWidth = 5,
			BackColor = Color.FromArgb(226, 231, 236)
		};
		split.Panel1.BackColor = Color.White;
		split.Panel1.Padding = new Padding(8);
		split.Panel2.BackColor = Color.White;
		split.Panel1.Controls.Add(_commandCategoryTree);
		split.Panel2.Controls.Add(_shortcutGrid);
		split.SizeChanged += delegate
		{
			if (split.Width > 560 && split.SplitterDistance < 230)
			{
				split.SplitterDistance = 230;
			}
		};
		return split;
	}

	private Control BuildAdvanceRow()
	{
		FlowLayoutPanel row = ToolbarRow();
		row.Padding = new Padding(0, 0, 0, 2);
		row.Controls.Add(_advanceEnabled);
		row.Controls.Add(new Label
		{
			Text = "按键",
			AutoSize = true,
			Margin = new Padding(4, 9, 0, 0),
			ForeColor = Color.FromArgb(82, 92, 102)
		});
		row.Controls.Add(_advanceKey);
		_advanceKey.KeyDown += delegate(object _, KeyEventArgs e)
		{
			SelectAdvanceKey(ComboAdvanceKeys.FromKeyCode(e.KeyCode));
			e.SuppressKeyPress = true;
		};
		return row;
	}

	private Control BuildNameRow()
	{
		TableLayoutPanel panel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			ColumnCount = 5,
			RowCount = 1
		};
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		panel.Controls.Add(new Label
		{
			Text = "组合名",
			Dock = DockStyle.Fill,
			ForeColor = Ink,
			Font = new Font(Font, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		_comboName.Dock = DockStyle.Fill;
		_comboName.Margin = new Padding(0, 7, 8, 6);
		panel.Controls.Add(_comboName, 1, 0);
		panel.Controls.Add(MakeButton("增加命令槽", AddEmptySlot, 92), 2, 0);
		panel.Controls.Add(MakeButton("删除当前槽", DeleteActiveSlot, 92), 3, 0);
		panel.Controls.Add(MakeButton("清空当前槽", ClearActiveSlot, 92), 4, 0);
		return panel;
	}

	private Control BuildSlotPane()
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			Padding = new Padding(0, 4, 0, 4)
		};
		panel.Controls.Add(_slotFlow);
		return panel;
	}

	private Control BuildShortcutToolbar()
	{
		TableLayoutPanel panel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			ColumnCount = 1,
			RowCount = 2
		};
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		FlowLayoutPanel actions = ToolbarRow();
		actions.Controls.Add(MakeButton("读取SW命令", ReadSolidWorksCommands, 112, primary: true));
		actions.Controls.Add(MakeButton("读取快捷键", ReadKeyboardList, 108));
		actions.Controls.Add(MakeButton("读取外部功能", ReadQinJianFunctions, 132));
		actions.Controls.Add(MakeButton("读取扩展功能", ReadJiaLiChuangFunctions, 112));
		actions.Controls.Add(MakeButton("读取外部插件", ReadThirdPartyPluginFunctions, 126));
		actions.Controls.Add(MakeButton("录制命令", RecordCommands, 100));

		TableLayoutPanel searchRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Margin = Padding.Empty
		};
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
		searchRow.Controls.Add(new Label
		{
			Text = "搜索",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		}, 0, 0);
		_search.Dock = DockStyle.Fill;
		_search.Margin = new Padding(0, 5, 10, 4);
		searchRow.Controls.Add(_search, 1, 0);
		searchRow.Controls.Add(_searchHint, 2, 0);
		panel.Controls.Add(actions, 0, 0);
		panel.Controls.Add(searchRow, 0, 1);
		_search.TextChanged += delegate
		{
			RefreshShortcutRows();
		};
		_search.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Enter) return;
			e.SuppressKeyPress = true;
			ResolveTypedFunction();
		};
		return panel;
	}

	private Control BuildActionBar()
	{
		TableLayoutPanel panel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			ColumnCount = 2,
			RowCount = 1
		};
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		FlowLayoutPanel moveActions = ToolbarRow();
		moveActions.Controls.Add(MakeButton("上移", () => MoveActiveSlot(-1), 64));
		moveActions.Controls.Add(MakeButton("下移", () => MoveActiveSlot(1), 64));
		moveActions.Controls.Add(MakeButton("步骤属性", EditActiveStep, 86));
		moveActions.Controls.Add(MakeButton("插入延时", () => InsertWaitStep(false), 86));
		moveActions.Controls.Add(MakeButton("插入等待确认", () => InsertWaitStep(true), 112));
		FlowLayoutPanel confirmActions = ToolbarRow(FlowDirection.RightToLeft);
		confirmActions.AutoSize = true;
		confirmActions.Controls.Add(MakeButton("确定", Accept, 82, primary: true));
		confirmActions.Controls.Add(MakeButton("取消", delegate
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}, 82));
		panel.Controls.Add(moveActions, 0, 0);
		panel.Controls.Add(confirmActions, 1, 0);
		return panel;
	}

	private void BuildShortcutGrid()
	{
		StyleGrid(_shortcutGrid);
		_shortcutGrid.Columns.Add("Hotkey", "快捷键/动作");
		_shortcutGrid.Columns.Add("Command", "命令");
		_shortcutGrid.Columns.Add("Category", "类别");
		_shortcutGrid.Columns[0].Width = 140;
		_shortcutGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
		_shortcutGrid.Columns[2].Width = 300;
		_shortcutGrid.DoubleClick += delegate
		{
			if (_shortcutGrid.CurrentRow?.Tag is KeyboardShortcutRow row)
			{
				ApplyShortcutToActiveSlot(row);
			}
		};
		_shortcutGrid.KeyDown += (_, e) =>
		{
			if (e.KeyCode != Keys.Enter || !(_shortcutGrid.CurrentRow?.Tag is KeyboardShortcutRow row)) return;
			e.Handled = true; e.SuppressKeyPress = true; ApplyShortcutToActiveSlot(row);
		};
	}

	private void RecordCommands()
	{
		if (_getSolidWorks() == null) { ToolNotice.Show(this, "请在 SOLIDWORKS 内打开组合编辑器后录制。", Text); return; }
		if (_recorder != null && !_recorder.IsDisposed) { _recorder.Activate(); return; }
		var recorder = new CommandRecorderWindow(ReadCommands());
		_recorder = recorder;
		recorder.ShowModeless(this, result =>
		{
			_recorder = null;
			if (!IsDisposed) Enabled = true;
			if (result != DialogResult.OK || IsDisposed) return;
			foreach (ComboSlotControl slot in _slots) slot.Dispose();
			_slots.Clear();
			foreach (var command in recorder.Recorded) AddSlot(command);
			if (recorder.Recorded.Any(command => command.RequireAdvanceKey || command.WaitMode == ComboWaitModes.ManualConfirm)) _advanceEnabled.Checked = true;
			RenderSlots(); SetActiveSlot(_slots.LastOrDefault());
		});
		Enabled = false;
	}

	private void EditActiveStep()
	{
		ComboCommandConfig command = _activeSlot?.ReadCommand();
		if (command == null) { ToolNotice.Show(this, "请先选中一条有效命令。", Text); return; }
		if (!CommandRecorderWindow.EditStepProperties(this, command)) return;
		_activeSlot.SetCommand(command);
		if (command.RequireAdvanceKey || command.WaitMode == ComboWaitModes.ManualConfirm) _advanceEnabled.Checked = true;
	}

	private void InsertWaitStep(bool manual)
	{
		int index = _activeSlot == null ? _slots.Count : _slots.IndexOf(_activeSlot) + 1;
		AddSlot(new ComboCommandConfig
		{
			Label = manual ? "等待手动确认" : "延时 800 毫秒",
			Kind = ActionKinds.Delay,
			Value = manual ? "0" : "800",
			WaitMode = manual ? ComboWaitModes.ManualConfirm : ComboWaitModes.Delay,
			DelayMs = manual ? 0 : 800
		});
		ComboSlotControl added = _slots[_slots.Count - 1];
		_slots.RemoveAt(_slots.Count - 1);
		_slots.Insert(index, added);
		if (manual) _advanceEnabled.Checked = true;
		RenderSlots();
		SetActiveSlot(added);
	}

	private void ResolveTypedFunction()
	{
		if (_activeSlot == null)
		{
			ToolNotice.Show(this, "请先点击上方要填入的命令框。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		ResolveTypedFunction(_activeSlot, _search.Text);
	}

	private void ResolveTypedFunction(ComboSlotControl slot, string input)
	{
		var candidates = FunctionNameResolver.FindCandidates(_shortcutRows, input);
		if (candidates.Count != 1)
		{
			_search.Text = input;
			if (_commandCategoryTree.Nodes.Count > 0) _commandCategoryTree.SelectedNode = _commandCategoryTree.Nodes[0];
			_shortcutGrid.Rows.Clear();
			foreach (var candidate in candidates)
			{
				int index = _shortcutGrid.Rows.Add(candidate.Hotkey, DisplayCommandName(candidate), ShortcutPaletteMatcher.SourceLabel(candidate));
				_shortcutGrid.Rows[index].Tag = candidate;
			}
			_shortcutGrid.ClearSelection();
			_searchHint.Text = candidates.Count == 0 ? "未找到功能，请先读取 SW 命令或外部功能列表。"
				: candidates.Count + " 个匹配功能：请双击目标来源，或用列表方向键选择后按 Enter。";
			return;
		}
		if (!ApplyShortcutToSlot(slot, candidates[0])) return;
		RenderSlots();
		SetActiveSlot(slot);
		_searchHint.Text = "已衔接：" + DisplayCommandName(candidates[0]) + " · " + ShortcutPaletteMatcher.SourceLabel(candidates[0]);
	}

	private void AddEmptySlot()
	{
		AddSlot(null);
		RenderSlots();
		SetActiveSlot(_slots.Last());
	}

	private void AddSlot(ComboCommandConfig? command)
	{
		ComboSlotControl slot = new ComboSlotControl(_slots.Count + 1);
		slot.Activated += delegate
		{
			SetActiveSlot(slot);
		};
		slot.PickRequested += delegate
		{
			SetActiveSlot(slot);
			PickForSlot(slot);
		};
		slot.AdvanceKeyRequired += delegate
		{
			_advanceEnabled.Checked = true;
		};
		slot.ResolveRequested += delegate { SetActiveSlot(slot); ResolveTypedFunction(slot, slot.TypedValue); };
		if (command != null)
		{
			slot.SetCommand(command);
		}
		_slots.Add(slot);
	}

	private void RenderSlots()
	{
		_slotFlow.SuspendLayout();
		_slotFlow.Controls.Clear();
		for (int i = 0; i < _slots.Count; i++)
		{
			_slots[i].StepNumber = i + 1;
			_slotFlow.Controls.Add(_slots[i]);
			if (i < _slots.Count - 1)
			{
				_slotFlow.Controls.Add(new Label
				{
					Text = "+",
					AutoSize = false,
					TextAlign = ContentAlignment.MiddleCenter,
					Width = 26,
					Height = _slots[i].Height,
					ForeColor = Color.FromArgb(245, 72, 84),
					Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Regular)
				});
			}
		}
		_slotFlow.ResumeLayout();
	}

	private void SetActiveSlot(ComboSlotControl? slot)
	{
		_activeSlot = slot;
		foreach (ComboSlotControl item in _slots)
		{
			item.IsActive = ReferenceEquals(item, slot);
		}
	}

	private void PickForSlot(ComboSlotControl slot)
	{
		if (slot.Kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{
			BasicActionPicker picker = new BasicActionPicker();
			picker.ShowModeless(this, result => { if (result == DialogResult.OK && !slot.IsDisposed) slot.SetBasic(picker.SelectedValue); });
			return;
		}
		if (slot.Kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			ToolNotice.Show(this, "在命令框里输入要点击的按钮文字，例如：条件选面、立即删除x。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		if (slot.Kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			ReadSolidWorksCommands();
			return;
		}
		if (slot.Kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			ReadQinJianFunctions();
			return;
		}
		if (slot.Kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			ReadJiaLiChuangFunctions();
			return;
		}
		if (slot.Kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetDelay("300");
			return;
		}
		if (_shortcutGrid.CurrentRow?.Tag is KeyboardShortcutRow row)
		{
			ApplyShortcutToSlot(slot, row);
			return;
		}
		if (_shortcutRows.Count == 0)
		{
			ReadKeyboardList();
		}
		ToolNotice.Show(this, "请先在下方快捷键列表中选中一条命令，或双击快捷键列表直接填入当前命令框。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void DeleteActiveSlot()
	{
		if (_activeSlot == null)
		{
			return;
		}
		int index = _slots.IndexOf(_activeSlot);
		_slots.Remove(_activeSlot);
		RenderSlots();
		SetActiveSlot(_slots.Count == 0 ? null : _slots[Math.Max(0, Math.Min(index, _slots.Count - 1))]);
	}

	private void ClearActiveSlot()
	{
		_activeSlot?.ClearCommand();
	}

	private void MoveActiveSlot(int delta)
	{
		if (_activeSlot == null)
		{
			return;
		}
		int oldIndex = _slots.IndexOf(_activeSlot);
		int newIndex = oldIndex + delta;
		if (newIndex < 0 || newIndex >= _slots.Count)
		{
			return;
		}
		_slots.RemoveAt(oldIndex);
		_slots.Insert(newIndex, _activeSlot);
		RenderSlots();
		SetActiveSlot(_activeSlot);
	}

	private void ReadKeyboardList()
	{
		if (!KeyboardListReader.TryRead(out List<KeyboardShortcutRow> rows, out string message))
		{
			ToolNotice.Show(this, message, "读取自定义键盘列表", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			SolidWorksNativeCommandCatalog.ClassifyShortcutRows(_getSolidWorks(), rows);
		}
		catch (Exception ex)
		{
			Logger.Trace("Classify combo shortcuts by toolbar failed: " + ex.Message);
		}
		rows = SolidWorksNativeCommandCatalog.ExcludeSeparatelyReadPluginShortcuts(rows);
		_shortcutRows = rows;
		RefreshShortcutRows();
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
			Logger.Error("Get SOLIDWORKS window for native combo command read failed", ex);
			ToolNotice.Show(this, ex.Message, "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		if (!SolidWorksNativeCommandCatalog.TryRead(solidWorks, out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Trace("Read SOLIDWORKS native combo command list skipped: " + message);
			ToolNotice.Show(this, message, "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		_search.Text = "SOLIDWORKS";
		RefreshShortcutRows();
		ToolNotice.Show(this, $"已识别 {rows.Count} 个 SOLIDWORKS 原生命令。双击可填入当前命令槽；执行仍需符合当前文档、选择对象和草图状态要求。", "读取SW命令", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
			Logger.Error("Get SOLIDWORKS window for QinJian combo read failed", ex);
		}
		if (!QinJianFunctionReader.TryRead(solidWorksWindow, out List<KeyboardShortcutRow> rows, out string message))
		{
			ToolNotice.Show(this, SanitizeExternalMessage(message), "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		if (!_search.Text.Equals("外部功能", StringComparison.OrdinalIgnoreCase))
		{
			_search.Text = "外部功能";
		}
		else
		{
			RefreshShortcutRows();
		}
		ToolNotice.Show(this, $"已读取 {rows.Count} 个外部功能。双击列表中的功能，可填入当前命令槽。", "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ReadJiaLiChuangFunctions()
	{
		if (!JiaLiChuangFunctionCatalog.TryRead(out List<KeyboardShortcutRow> rows, out string message))
		{
			Logger.Error("Read JiaLiChuang Ican combo function list failed: " + message);
			ToolNotice.Show(this, SanitizeExternalMessage(message), "读取扩展功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		if (!_search.Text.Equals("扩展功能", StringComparison.OrdinalIgnoreCase))
		{
			_search.Text = "扩展功能";
		}
		else
		{
			RefreshShortcutRows();
		}
		ToolNotice.Show(this, $"已读取 {rows.Count} 个扩展功能。双击列表中的功能，可填入当前命令槽。", "读取扩展功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ReadThirdPartyPluginFunctions()
	{
		if (!ExternalPluginCommandCatalog.TryRead(_getSolidWorks(), _getCommandManager(), out List<KeyboardShortcutRow> rows, out string message))
		{
			// CommandManager/toolbar scans can be empty while a plugin exposes
			// its controls only through the visible task pane. Keep the same UI
			// fallback as the main settings page so the combo editor does not
			// report a false "no functions" result.
			IntPtr window = IntPtr.Zero;
			try { window = _getSolidWorksWindowHandle(); } catch (Exception ex) { Logger.Trace("Get SOLIDWORKS window for external combo read skipped: " + ex.Message); }
			if (!QinJianFunctionReader.TryReadExternalPlugins(window, out rows, out string uiMessage))
			{
				ToolNotice.Show(this, SanitizeExternalMessage(message + " " + uiMessage), "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			message = uiMessage;
		}
		_shortcutRows = MergeShortcutRows(_shortcutRows, rows);
		_search.Text = "外部功能";
		// TextChanged is not raised when the same filter is read twice. Always
		// refresh after replacing the catalog so a second external read cannot
		// leave the previous (or empty) grid visible.
		RefreshShortcutRows();
		ToolNotice.Show(this, SanitizeExternalMessage(message) + " 双击列表功能可填入当前命令槽。", "读取外部功能", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
	}

	private void ApplyShortcutToActiveSlot(KeyboardShortcutRow shortcut)
	{
		if (_activeSlot == null)
		{
			ToolNotice.Show(this, "请先点击上方要填入的命令框。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		ApplyShortcutToSlot(_activeSlot, shortcut);
	}

	private bool ApplyShortcutToSlot(ComboSlotControl slot, KeyboardShortcutRow shortcut)
	{
		if (!ShortcutPaletteMatcher.TryGetAction(shortcut, out _, out _))
		{ _searchHint.Text = "该候选没有有效命令身份，请重新读取命令。"; return false; }
		string displayName = DisplayCommandName(shortcut);
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
		{ slot.SetBasic(ShortcutActionValue(shortcut)); return true; }
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetCommand(new ComboCommandConfig { Kind = ActionKinds.ExternalPlugin, Label = displayName, Value = ShortcutActionValue(shortcut), RequireAdvanceKey = true });
			return true;
		}
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetNativeCommand(displayName, ShortcutActionValue(shortcut));
			return true;
		}
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetQinJian(displayName, ShortcutActionValue(shortcut));
			return true;
		}
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetJiaLiChuang(displayName, ShortcutActionValue(shortcut));
			return true;
		}
		if (ShortcutActionKind(shortcut).Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
		{
			slot.SetUiClick(ShortcutActionValue(shortcut));
			return true;
		}
		if (ShortcutChangeDetector.HasConflict(_shortcutRows, shortcut?.Hotkey, out ShortcutConflictResult conflict))
		{
			ToolNotice.Show(this,
				$"快捷键 {conflict.Hotkey} 同时对应以下命令：\r\n{string.Join("\r\n", conflict.Commands)}\r\n\r\n请先在 SOLIDWORKS 自定义键盘中消除冲突。",
				"快捷键冲突",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return false;
		}
		slot.SetShortcut(new KeyboardShortcutRow
		{
			Category = shortcut.Category,
			Command = displayName,
			Hotkey = shortcut.Hotkey,
			ActionKind = shortcut.ActionKind,
			Value = shortcut.Value
		});
		return true;
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
		if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase)) return ActionKinds.Basic;
		if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.ExternalPlugin;
		}
		if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
		{
			return ActionKinds.QinJian;
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

	private void Accept()
	{
		foreach (ComboSlotControl slot in _slots)
		{
			if (!slot.TryResolveTypedFunction(_shortcutRows, out string message))
			{
				if (message.Length > 0)
				{
					ToolNotice.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					return;
				}
			}
		}
		List<ComboCommandConfig> commands = ReadCommands();
		if (commands.Count == 0)
		{
			ToolNotice.Show(this, "组合触发至少需要一条命令。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		Commands = commands;
		ComboAdvanceEnabled = _advanceEnabled.Checked;
		ComboAdvanceKey = ComboAdvanceKeys.Normalize(Convert.ToString(_advanceKey.SelectedItem));
		ComboName = _comboName.Text.Trim();
		if (string.IsNullOrWhiteSpace(ComboName))
		{
			ComboName = commands.Count == 1 ? (commands[0].Label.Length == 0 ? commands[0].Value : commands[0].Label) : "组合触发";
		}
		DialogResult = DialogResult.OK;
		Close();
	}

	private void SelectAdvanceKey(string value)
	{
		string normalized = ComboAdvanceKeys.Normalize(value);
		if (!_advanceKey.Items.Contains(normalized))
		{
			_advanceKey.Items.Add(normalized);
		}
		_advanceKey.SelectedItem = normalized;
	}

	private List<ComboCommandConfig> ReadCommands()
	{
		List<ComboCommandConfig> commands = new List<ComboCommandConfig>();
		foreach (ComboSlotControl slot in _slots)
		{
			ComboCommandConfig? command = slot.ReadCommand();
			if (command != null)
			{
				commands.Add(command);
			}
		}
		return commands;
	}

	private void RefreshShortcutRows()
	{
		RefreshCommandCategoryTree();
		string keyword = _search.Text.Trim();
		string category = Convert.ToString(_commandCategoryTree.SelectedNode?.Tag) ?? "";
		_shortcutGrid.Rows.Clear();
		foreach (KeyboardShortcutRow row in _shortcutRows
			.Where(item => CategoryMatches(item.Category, category))
			.Where(row => string.IsNullOrWhiteSpace(keyword)
				|| (row.Command ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
				|| (row.Hotkey ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
				|| (row.Category ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
				// The toolbar buttons use friendly aliases (外部功能/扩展功能)
				// while their persisted category keeps the vendor name. Include
				// the display category so the just-read list remains selectable.
				|| DisplayCategory(row).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
				|| (ShortcutActionKind(row).Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase)
					&& "扩展功能".IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
				|| DisplayCommandName(row).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
		{
			int index = _shortcutGrid.Rows.Add(row.Hotkey, DisplayCommandName(row), DisplayCategory(row));
			_shortcutGrid.Rows[index].Tag = row;
		}
	}

	private string DisplayCommandName(KeyboardShortcutRow row)
	{
		string displayName = (_getDisplayName(row) ?? "").Trim();
		return displayName.Length == 0 ? (row?.Command ?? "") : displayName;
	}

	private void RefreshCommandCategoryTree()
	{
		string fingerprint = string.Join("\u001f", _shortcutRows.Select(DisplayCategory).Where(value => value.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase));
		if (fingerprint == _categoryFingerprint && _commandCategoryTree.Nodes.Count > 0)
		{
			return;
		}
		_categoryFingerprint = fingerprint;
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
		_commandCategoryTree.SelectedNode = FindCategoryNode(root, selected) ?? root;
		FindCategoryNode(root, "SOLIDWORKS")?.Expand();
		_commandCategoryTree.EndUpdate();
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

	private static bool CategoryMatches(string rowCategory, string selectedCategory)
	{
		if (string.IsNullOrWhiteSpace(selectedCategory))
		{
			return true;
		}
		string category = (rowCategory ?? "").Trim();
		if (selectedCategory.Equals("外部功能", StringComparison.CurrentCultureIgnoreCase))
			return DisplayCategory(new KeyboardShortcutRow { Category = category }).StartsWith("外部功能", StringComparison.CurrentCultureIgnoreCase);
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
			string path = raw;
			if (path.StartsWith("第三方插件 > ", StringComparison.CurrentCultureIgnoreCase))
				path = path.Substring("第三方插件 > ".Length).Trim();
			if (path.StartsWith("外部功能 > ", StringComparison.CurrentCultureIgnoreCase))
				path = path.Substring("外部功能 > ".Length).Trim();
			return path.Length == 0 ? "外部功能" : "外部功能 > " + path;
		}
		return (row?.Category ?? "").Trim();
	}

	private static Button MakeButton(string text, Action action, int width = 86, bool primary = false)
	{
		Button button = new Button
		{
			Text = text,
			Width = width,
			Height = 28,
			FlatStyle = FlatStyle.Flat,
			BackColor = primary ? Color.FromArgb(65, 139, 217) : Color.White,
			ForeColor = primary ? Color.White : Color.FromArgb(35, 45, 55),
			Margin = new Padding(4, 4, 4, 4)
		};
		button.FlatAppearance.BorderColor = primary ? Color.FromArgb(65, 139, 217) : Color.FromArgb(210, 216, 222);
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	private static string SanitizeExternalMessage(string message)
		=> (message ?? "").Replace("琴剑", "外部功能").Replace("嘉立创", "扩展功能").Replace("启翔铝型材", "外部型材功能").Replace("博士", "外部工具").Replace("Doctor", "外部工具").Replace("doctor", "外部工具");

	private static FlowLayoutPanel ToolbarRow(FlowDirection direction = FlowDirection.LeftToRight)
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = direction,
			WrapContents = false,
			Margin = Padding.Empty,
			BackColor = Color.White
		};
	}

	private static void StyleGrid(DataGridView grid)
	{
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
			StyleInteractiveControls(control);
		}
	}

	private sealed class ComboSlotControl : UserControl
	{
		private readonly Label _step = new Label
		{
			AutoSize = true,
			Location = new Point(10, 8),
			ForeColor = Color.FromArgb(82, 92, 102)
		};

		private readonly ComboBox _kind = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Location = new Point(66, 5),
			Width = 112
		};

		private readonly TextBox _label = new TextBox
		{
			Location = new Point(10, 42),
			Width = 178
		};

		private readonly TextBox _value = new TextBox
		{
			Location = new Point(10, 70),
			Width = 178,
			Height = 24
		};

		private readonly CheckBox _autoConfirm = new CheckBox
		{
			Text = "自动确定",
			Location = new Point(12, 100),
			AutoSize = true,
			ForeColor = Accent
		};

		private readonly CheckBox _requireAdvanceKey = new CheckBox
		{
			Text = "按键衔接",
			Location = new Point(104, 100),
			AutoSize = true,
			ForeColor = Color.FromArgb(65, 139, 217)
		};

		private bool _isActive;
		private string _commandValue = "";
		private string _persistedIdentityKind = "";
		private string _persistedIdentityValue = "";
		// Keep options that are not shown in the compact slot card. Recorded
		// steps may carry explicit wait/retry/selection settings; rebuilding a
		// command from the visible fields must not drop those settings.
		private string _advanceKey = "";
		private string _waitMode = ComboWaitModes.None;
		private int _delayMs;
		private int _timeoutMs;
		private string _onError = ComboErrorPolicies.Stop;
		private int _retryCount;
		private bool _saveSelectionBefore;
		private bool _restoreSelectionAfter;

		public event EventHandler? Activated;

		public event EventHandler? PickRequested;

		public event EventHandler? AdvanceKeyRequired;
		public event EventHandler? ResolveRequested;
		public string TypedValue => (_commandValue ?? "").Trim();

		public ComboSlotControl(int stepNumber)
		{
			StepNumber = stepNumber;
			Width = 204;
			Height = 132;
			Margin = new Padding(4);
			BackColor = Color.FromArgb(252, 253, 254);
			DoubleBuffered = true;
			_kind.Items.Add("快捷键");
			_kind.Items.Add("SW 基础功能");
			_kind.Items.Add("SW 原生命令");
			_kind.Items.Add("点击按钮");
			_kind.Items.Add("外部功能");
			_kind.Items.Add("扩展功能");
			_kind.Items.Add("外部插件功能");
			_kind.Items.Add("等待 ms");
			_kind.SelectedIndex = 0;
			Controls.Add(_step);
			Controls.Add(_kind);
			Controls.Add(_label);
			Controls.Add(_value);
			Controls.Add(_autoConfirm);
			Controls.Add(_requireAdvanceKey);
			ToolTip requireAdvanceTip = new ToolTip();
			requireAdvanceTip.SetToolTip(_requireAdvanceKey, "仅在未勾选自动确定时使用；勾选后必须按衔接键才进入下一步");
			ToolTip commandInputTip = new ToolTip();
			commandInputTip.SetToolTip(_label, "显示名称；也可在下方值中直接输入功能名");
			commandInputTip.SetToolTip(_value, "快捷键或功能名；确定时会自动匹配已读取的命令");
			_autoConfirm.CheckedChanged += delegate
			{
				if (_autoConfirm.Checked)
				{
					_requireAdvanceKey.Checked = false;
				}
			};
			_requireAdvanceKey.CheckedChanged += delegate
			{
				ActivateSlot();
				if (_requireAdvanceKey.Checked)
				{
					_autoConfirm.Checked = false;
					AdvanceKeyRequired?.Invoke(this, EventArgs.Empty);
				}
			};
			_kind.SelectedIndexChanged += delegate
			{
				ActivateSlot();
				if (Kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase) && !BasicActions.Contains(_commandValue))
				{
					_commandValue = "";
					_label.Clear();
					UpdateValueLabel();
				}
			};
			_label.Enter += delegate { ActivateSlot(); };
			_label.Click += delegate { ActivateSlot(); };
			_value.Enter += delegate { ActivateSlot(); };
			_value.Click += delegate { ActivateSlot(); };
			_value.TextChanged += delegate
			{
				_commandValue = _value.Text;
			};
			_value.KeyDown += (_, e) =>
			{
				if (e.KeyCode != Keys.Enter) return;
				e.Handled = true; e.SuppressKeyPress = true; ResolveRequested?.Invoke(this, EventArgs.Empty);
			};
			_label.DoubleClick += delegate
			{
				ActivateSlot();
				PickRequested?.Invoke(this, EventArgs.Empty);
			};
			Click += delegate { ActivateSlot(); };
			foreach (Control control in Controls)
			{
				control.Click += delegate { ActivateSlot(); };
			}
			UpdateValueLabel();
		}

		public int StepNumber
		{
			get => int.TryParse(_step.Tag as string, out int value) ? value : 0;
			set
			{
				_step.Tag = value.ToString();
				_step.Text = "第 " + value + " 步";
			}
		}

		public bool IsActive
		{
			get => _isActive;
			set
			{
				_isActive = value;
				Invalidate();
			}
		}

		public string Kind => DisplayToKind(Convert.ToString(_kind.SelectedItem));

		public void SetCommand(ComboCommandConfig command)
		{
			string kind = (command.Kind ?? "").Trim();
			if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "SW 基础功能";
			}
			else if (kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "SW 原生命令";
			}
			else if (kind.Equals(ActionKinds.UiClick, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "点击按钮";
			}
			else if (kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "外部功能";
			}
			else if (kind.Equals(ActionKinds.JiaLiChuang, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "扩展功能";
			}
			else if (kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "外部插件功能";
			}
			else if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase))
			{
				_kind.SelectedItem = "等待 ms";
			}
			else
			{
				_kind.SelectedItem = "快捷键";
			}
			_commandValue = command.Value ?? "";
			_persistedIdentityKind = Kind;
			_persistedIdentityValue = _commandValue.Trim();
			_advanceKey = command.AdvanceKey ?? "";
			_waitMode = ComboWaitModes.Normalize(command.WaitMode);
			_delayMs = command.DelayMs;
			_timeoutMs = command.TimeoutMs;
			_onError = ComboErrorPolicies.Normalize(command.OnError);
			_retryCount = command.RetryCount;
			_saveSelectionBefore = command.SaveSelectionBefore;
			_restoreSelectionAfter = command.RestoreSelectionAfter;
			_label.Text = command.Label ?? "";
			if (string.IsNullOrWhiteSpace(_label.Text))
			{
				_label.Text = Kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase) ? BasicActions.Display(_commandValue) : _commandValue;
			}
			_autoConfirm.Checked = command.AutoConfirm;
			_requireAdvanceKey.Checked = command.RequireAdvanceKey;
			UpdateValueLabel();
		}

		public void SetShortcut(KeyboardShortcutRow shortcut)
		{
			ResetStepOptions();
			_kind.SelectedItem = "快捷键";
			_label.Text = shortcut.Command ?? "";
			_commandValue = shortcut.Hotkey ?? "";
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetBasic(string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "SW 基础功能";
			_commandValue = value ?? "";
			_label.Text = BasicActions.Display(_commandValue);
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetNativeCommand(string label, string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "SW 原生命令";
			_commandValue = value ?? "";
			_label.Text = label ?? "";
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetUiClick(string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "点击按钮";
			_commandValue = value ?? "";
			_label.Text = value ?? "";
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetQinJian(string label, string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "外部功能";
			_commandValue = value ?? "";
			_label.Text = label ?? "";
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetJiaLiChuang(string label, string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "扩展功能";
			_commandValue = value ?? "";
			_label.Text = label ?? "";
			_requireAdvanceKey.Checked = true;
			UpdateValueLabel();
		}

		public void SetDelay(string value)
		{
			ResetStepOptions();
			_kind.SelectedItem = "等待 ms";
			_commandValue = value ?? "";
			_label.Text = value ?? "";
			UpdateValueLabel();
		}

		public bool TryResolveTypedFunction(IEnumerable<KeyboardShortcutRow> rows, out string message)
		{
			message = "";
			string typed = (_commandValue ?? "").Trim();
			if (typed.Length == 0 || Kind == ActionKinds.Delay || Kind == ActionKinds.UiClick) return true;
			if (Kind == ActionKinds.Hotkey && ShortcutPaletteMatcher.IsHotkey(typed)) return true;
			if (Kind == ActionKinds.Basic && BasicActions.Contains(typed)) return true;
			if (Kind == ActionKinds.NativeCommand && (NativeCommandReference.TryDecodeId(typed, out _) || NativeCommandReference.TryDecode(typed, out _, out _) || ExternalToolbarCommandReference.TryDecode(typed, out _, out _))) return true;
			if (HasPersistedExternalIdentity(typed)) return true;
			if ((rows ?? Enumerable.Empty<KeyboardShortcutRow>()).Any(row => string.Equals(row.ActionKind, Kind, StringComparison.OrdinalIgnoreCase) && string.Equals(row.Value, typed, StringComparison.Ordinal) && ShortcutPaletteMatcher.IsNameCandidate(row))) return true;
			if (!FunctionNameResolver.TryResolve(rows, typed, out KeyboardShortcutRow resolved, out message)) return false;
			SetResolvedCommand(resolved);
			return true;
		}

		private bool HasPersistedExternalIdentity(string value)
		{
			// A saved command is already an identity, not a function-name query.
			// Its executor still validates the currently loaded add-in at replay.
			if (Kind == ActionKinds.ExternalPlugin)
			{
				try
				{
					return ExternalToolbarCommandReference.TryDecode(value, out _, out _)
						|| ExternalNamedCommandReference.TryDecode(value, out _, out _, out _)
						|| ExternalPluginActionValue.TryParse(value, out _, out _);
				}
				catch (UriFormatException) { return false; }
			}
			if (Kind == ActionKinds.QinJian) return QinJianActionValue.TryParse(value, out _, out _);
			// This older adapter stores a callback name rather than an encoded
			// reference. Preserve only its unchanged loaded identity; newly typed
			// names must resolve through the catalog and cannot invent callbacks.
			return Kind == ActionKinds.JiaLiChuang && _persistedIdentityKind == Kind
				&& value == _persistedIdentityValue && value.Length > 0
				&& (char.IsLetter(value[0]) || value[0] == '_')
				&& value.All(character => char.IsLetterOrDigit(character) || character == '_')
				&& value.Any(character => character >= '\u3400' && character <= '\u9fff' || character >= '\uf900' && character <= '\ufaff');
		}

		private void SetResolvedCommand(KeyboardShortcutRow row)
		{
			string kind = ShortcutActionKind(row);
			string value = ShortcutActionValue(row);
			string label = row.Command ?? value;
			var command = ReadCommand() ?? new ComboCommandConfig { RequireAdvanceKey = true };
			command.Kind = kind; command.Value = value; command.Label = label;
			SetCommand(command);
		}

		private static bool LooksLikeHotkey(string value)
		{
			string text = (value ?? "").Trim();
			if (text.IndexOf('+') >= 0 || text.Equals("Space", StringComparison.OrdinalIgnoreCase)
				|| text.Equals("Enter", StringComparison.OrdinalIgnoreCase) || text.Equals("Tab", StringComparison.OrdinalIgnoreCase)
				|| text.Equals("Esc", StringComparison.OrdinalIgnoreCase)) return true;
			return text.Length >= 2 && (text[0] == 'F' || text[0] == 'f') && int.TryParse(text.Substring(1), out _);
		}

		public void ClearCommand()
		{
			ResetStepOptions();
			_kind.SelectedItem = "快捷键";
			_label.Clear();
			_commandValue = "";
			_autoConfirm.Checked = false;
			_requireAdvanceKey.Checked = false;
			UpdateValueLabel();
		}

		public ComboCommandConfig? ReadCommand()
		{
			string kind = Kind;
			string label = _label.Text.Trim();
			string value = _commandValue.Trim();
			if (string.IsNullOrWhiteSpace(value))
			{
				value = label;
			}
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}
			if (kind.Equals(ActionKinds.Basic, StringComparison.OrdinalIgnoreCase) && !BasicActions.Contains(value))
			{
				return null;
			}
			if (kind.Equals(ActionKinds.Delay, StringComparison.OrdinalIgnoreCase)
				&& (!int.TryParse(value, out int delayValue) || delayValue < 0 || delayValue > 600000))
			{
				return null;
			}
			return new ComboCommandConfig
			{
				Kind = kind,
				Label = label,
				Value = value,
				AutoConfirm = _autoConfirm.Checked,
				RequireAdvanceKey = _requireAdvanceKey.Checked,
				AdvanceKey = _advanceKey ?? "",
				WaitMode = ComboWaitModes.Normalize(_waitMode),
				DelayMs = Math.Max(0, _delayMs),
				TimeoutMs = Math.Max(0, _timeoutMs),
				OnError = ComboErrorPolicies.Normalize(_onError),
				RetryCount = Math.Max(0, _retryCount),
				SaveSelectionBefore = _saveSelectionBefore,
				RestoreSelectionAfter = _restoreSelectionAfter && _saveSelectionBefore
			};
		}

		private void ResetStepOptions()
		{
			_persistedIdentityKind = "";
			_persistedIdentityValue = "";
			_advanceKey = "";
			_waitMode = ComboWaitModes.None;
			_delayMs = 0;
			_timeoutMs = 0;
			_onError = ComboErrorPolicies.Stop;
			_retryCount = 0;
			_saveSelectionBefore = false;
			_restoreSelectionAfter = false;
			_autoConfirm.Checked = false;
			_requireAdvanceKey.Checked = false;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Color border = _isActive ? Accent : Border;
			using Pen pen = new Pen(border, _isActive ? 2f : 1f);
			Rectangle rect = ClientRectangle;
			rect.Width -= 1;
			rect.Height -= 1;
			e.Graphics.DrawRectangle(pen, rect);
		}

		private void ActivateSlot()
		{
			Activated?.Invoke(this, EventArgs.Empty);
		}

		private void UpdateValueLabel()
		{
			if (!string.Equals(_value.Text, _commandValue ?? "", StringComparison.Ordinal))
			{
				_value.Text = _commandValue ?? "";
			}
		}

		private static string DisplayToKind(string value)
		{
			string text = (value ?? "").Trim();
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
			if (text.Equals("扩展功能", StringComparison.OrdinalIgnoreCase) || text.Equals("嘉立创功能", StringComparison.OrdinalIgnoreCase))
			{
				return ActionKinds.JiaLiChuang;
			}
			if (text.Equals("外部插件功能", StringComparison.OrdinalIgnoreCase))
			{
				return ActionKinds.ExternalPlugin;
			}
			if (text.Equals("等待 ms", StringComparison.OrdinalIgnoreCase))
			{
				return ActionKinds.Delay;
			}
			return ActionKinds.Hotkey;
		}
	}
}
