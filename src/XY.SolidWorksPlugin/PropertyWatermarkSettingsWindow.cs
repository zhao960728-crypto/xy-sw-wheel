using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace XY.SolidWorksPlugin;

internal sealed class PropertyWatermarkSettingsWindow : PluginToolWindow
{
	private readonly Func<PluginConfig> _load;
	private readonly Action<PluginConfig> _save;
	private readonly Action _refreshWatermark;
	private readonly Func<ISldWorks> _getSolidWorks;
	private readonly NumericUpDown _fontSize = new NumericUpDown { Minimum = 7, Maximum = 22, DecimalPlaces = 1, Increment = 0.5m, Width = 70 };
	private readonly NumericUpDown _opacity = new NumericUpDown { Minimum = 20, Maximum = 100, Width = 58 };
	private readonly NumericUpDown _displayScale = new NumericUpDown { Minimum = 50, Maximum = 200, Width = 58 };
	private readonly Button _textColor = new Button { Text = "文字颜色", Width = 92, Height = 28, FlatStyle = FlatStyle.Flat };
	private readonly CheckBox _moveEnabled = new CheckBox { Text = "开启移动", AutoSize = true };
	private readonly CheckBox _readSelectedPart = new CheckBox { Text = "读取选择零件参数", AutoSize = true };
	private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoGenerateColumns = false };
	private PluginConfig _config;
	private PropertyWatermarkConfig _baseline;
	private readonly Label _readStatus = new Label { AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 8, 8, 0) };

	public PropertyWatermarkSettingsWindow(Func<PluginConfig> load, Action<PluginConfig> save, Func<ISldWorks> getSolidWorks, Action refreshWatermark)
	{
		_load = load; _save = save; _getSolidWorks = getSolidWorks; _refreshWatermark = refreshWatermark; _config = _load();
		_config = new PluginConfig { PropertyWatermark = _config.PropertyWatermark.Clone() };
		Text = "属性水印设置"; StartPosition = FormStartPosition.CenterParent; Size = new Size(1120, 680); MinimumSize = new Size(900, 560);
		Font = new Font("Microsoft YaHei UI", 9f); BackColor = Color.FromArgb(248, 250, 252);
		BuildGrid(); LoadConfig();
		_baseline = _config.PropertyWatermark.Clone();
		PreserveAppearance(_textColor);
		Controls.Add(BuildLayout());
		_readSelectedPart.CheckedChanged += (_, __) => RefreshReadValues();
		// Rows are materialized only after the bound grid has a window/binding
		// context. Reading solely in the constructor leaves the first preview blank.
		Shown += (_, __) => RefreshReadValues();
	}

	private Control BuildLayout()
	{
		TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
		root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Label hint = new Label { AutoSize = true, Text = "水印吸附在 SOLIDWORKS 当前窗口左下角，实时显示当前文档和配置专属属性。可直接增删字段、调整顺序。显示总开关统一在“轮盘设置 → 功能开关”中管理。", ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 0, 0, 10) };
		FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
		options.Controls.Add(_moveEnabled); options.Controls.Add(_readSelectedPart);
		options.Controls.Add(new Label { Text = "字体：宋体", AutoSize = true, Margin = new Padding(20, 7, 2, 0) });
		options.Controls.Add(new Label { Text = "字号", AutoSize = true, Margin = new Padding(16, 7, 2, 0) }); options.Controls.Add(_fontSize);
		options.Controls.Add(new Label { Text = "透明度 %", AutoSize = true, Margin = new Padding(16, 7, 2, 0) }); options.Controls.Add(_opacity); options.Controls.Add(_textColor);
		var scaleOption = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 0, 0, 0) };
		scaleOption.Controls.Add(new Label { Text = "显示大小 %", AutoSize = true, Margin = new Padding(0, 7, 2, 0) }); scaleOption.Controls.Add(_displayScale); options.Controls.Add(scaleOption);
		_textColor.Click += (_, __) => PickTextColor();
		TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 10) };
		header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		header.Controls.Add(hint, 0, 0); header.Controls.Add(options, 0, 1);
		FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 8) };
		actions.Controls.Add(Button("添加属性", AddField)); actions.Controls.Add(Button("删除选中", DeleteSelected)); actions.Controls.Add(Button("上移", () => MoveSelected(-1))); actions.Controls.Add(Button("下移", () => MoveSelected(1)));
		actions.Controls.Add(Button("读取当前属性", ReadCurrentProperties, true));
		actions.Controls.Add(Button("刷新属性", RefreshProperties));
		FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
		footer.Controls.Add(Button("取消", Close)); footer.Controls.Add(Button("保存设置", Save, true));
		footer.Controls.Add(_readStatus);
		root.Controls.Add(header, 0, 0); root.Controls.Add(_grid, 0, 1); root.Controls.Add(actions, 0, 2); root.Controls.Add(footer, 0, 3);
		return root;
	}

	private Button Button(string text, Action action, bool primary = false)
	{
		Button button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(30, 136, 229) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(35, 45, 55), Margin = new Padding(0, 0, 8, 0) };
		button.FlatAppearance.BorderColor = primary ? button.BackColor : Color.FromArgb(210, 216, 222); button.Click += (_, __) => action(); return button;
	}

	private void BuildGrid()
	{
		_grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Enabled", HeaderText = "启用", Width = 55 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "DisplayName", HeaderText = "显示名称", Width = 180 });
		DataGridViewComboBoxColumn source = new DataGridViewComboBoxColumn { DataPropertyName = "Source", HeaderText = "来源类型", Width = 150 };
		source.Items.AddRange("原生属性", "自定义属性"); _grid.Columns.Add(source);
		_grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PropertyName", HeaderText = "属性名称", Width = 260 });
		_grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ResolvedValue", HeaderText = "读取到的属性内容", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
		_grid.CellValueChanged += (_, e) => ApplySourcePreset(e);
	}

	private void LoadConfig()
	{
		PropertyWatermarkConfig.Normalize(_config.PropertyWatermark);
		_moveEnabled.Checked = _config.PropertyWatermark.MoveEnabled; _readSelectedPart.Checked = _config.PropertyWatermark.ReadSelectedPartProperties;
		_fontSize.Value = (decimal)_config.PropertyWatermark.FontSize;
		_opacity.Value = _config.PropertyWatermark.OpacityPercent;
		_displayScale.Value = _config.PropertyWatermark.DisplayScalePercent;
		SetTextColorButton(ParseColor(_config.PropertyWatermark.TextColor));
		_grid.DataSource = new BindingSource { DataSource = _config.PropertyWatermark.Fields };
		RefreshReadValues();
	}

	private List<PropertyWatermarkField> Fields() => (List<PropertyWatermarkField>)((BindingSource)_grid.DataSource).DataSource;
	private void ResetFields() => ((BindingSource)_grid.DataSource).ResetBindings(false);
	private void AddField() { if (Fields().Count >= 20) { _readStatus.Text = "最多显示 20 个字段，请先删除不需要的字段。"; return; } Fields().Add(new PropertyWatermarkField { DisplayName = "新属性", Source = "自定义属性", PropertyName = "属性名" }); ResetFields(); _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[1]; _grid.BeginEdit(true); }
	private void DeleteSelected() { if (_grid.CurrentRow != null) { Fields().RemoveAt(_grid.CurrentRow.Index); ResetFields(); } }
	private void MoveSelected(int direction)
	{
		if (_grid.CurrentRow == null) return; int index = _grid.CurrentRow.Index; int target = index + direction; List<PropertyWatermarkField> fields = Fields();
		if (target < 0 || target >= fields.Count) return; PropertyWatermarkField field = fields[index]; fields.RemoveAt(index); fields.Insert(target, field); ResetFields(); _grid.CurrentCell = _grid.Rows[target].Cells[1];
	}

	private void ReadCurrentProperties()
	{
		try
		{
			IModelDoc2 doc = WatermarkPropertyAccess.ResolveDocument(_getSolidWorks(), _readSelectedPart.Checked, out string configuration);
			if (doc == null) { ToolNotice.Show(this, "请先打开 SOLIDWORKS 模型。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
			List<string> names = new[] { configuration, "" }.Distinct().SelectMany(scope =>
				(doc.Extension.get_CustomPropertyManager(scope).GetNames() as object[] ?? Array.Empty<object>()).Select(Convert.ToString)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
			if (names.Count == 0) { ToolNotice.Show(this, "当前模型没有可读取的自定义属性。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
			PluginToolWindow chooser = new PluginToolWindow { Text = "当前模型属性", ClientSize = new Size(390, 460), StartPosition = FormStartPosition.CenterParent, Font = Font, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
			CheckedListBox list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true }; list.Items.AddRange(names.Cast<object>().ToArray());
			HashSet<string> defaults = new HashSet<string>(Fields().Where(f => f.Source == "自定义属性").Select(f => f.PropertyName), StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, defaults.Contains(list.Items[i].ToString()));
			TableLayoutPanel pickerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 2 };
			pickerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); pickerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
			FlowLayoutPanel pickerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
			pickerActions.Controls.Add(Button("全选", () => SetAllChecked(list, true)));
			pickerActions.Controls.Add(Button("反选", () => InvertChecked(list)));
			Button add = Button("加入水印", () => chooser.DialogResult = DialogResult.OK, true); add.Margin = new Padding(24, 0, 0, 0); pickerActions.Controls.Add(add);
			pickerActions.Controls.Add(Button("取消", () => chooser.DialogResult = DialogResult.Cancel));
			pickerLayout.Controls.Add(list, 0, 0); pickerLayout.Controls.Add(pickerActions, 0, 1); chooser.Controls.Add(pickerLayout);
			chooser.ShowModeless(this, result => { if (result != DialogResult.OK) return;
			var additions = list.CheckedItems.Cast<string>().Where(name => !Fields().Any(f => f.Source == "自定义属性" && string.Equals(f.PropertyName, name, StringComparison.OrdinalIgnoreCase))).ToList();
			if (Fields().Count + additions.Count > 20) { _readStatus.Text = "字段超过 20 个，请减少勾选后重新加入；未截断或保存。"; return; }
			foreach (string name in additions) Fields().Add(new PropertyWatermarkField { DisplayName = name, Source = "自定义属性", PropertyName = name });
			ResetFields();
			RefreshReadValues();
			});
		}
		catch (Exception ex) { Logger.Error("Read property watermark fields failed", ex); ToolNotice.Show(this, "读取属性失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
	}

	private static void SetAllChecked(CheckedListBox list, bool checkedValue)
	{
		for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, checkedValue);
	}

	private static void InvertChecked(CheckedListBox list)
	{
		for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, !list.GetItemChecked(i));
	}

	private void ApplySourcePreset(DataGridViewCellEventArgs e)
	{
		if (e.RowIndex < 0 || e.ColumnIndex != 2 || _grid.DataSource == null) return;
		PropertyWatermarkField field = Fields()[e.RowIndex];
		ResetFields();
	}

	private void PickTextColor()
	{
		using ColorDialog dialog = new ColorDialog { Color = ParseColor(_config.PropertyWatermark.TextColor), FullOpen = true };
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		_config.PropertyWatermark.TextColor = ColorTranslator.ToHtml(dialog.Color);
		SetTextColorButton(dialog.Color);
	}

	private static Color ParseColor(string value)
	{
		try { return ColorTranslator.FromHtml(value); } catch { return Color.FromArgb(30, 41, 59); }
	}

	private void SetTextColorButton(Color color)
	{
		_textColor.BackColor = color; _textColor.ForeColor = color.GetBrightness() < 0.55f ? Color.White : Color.Black;
		_textColor.FlatAppearance.BorderColor = color;
	}

	private void RefreshProperties()
	{
		if (!_grid.EndEdit()) return;
		((BindingSource)_grid.DataSource).EndEdit();
		RefreshReadValues();
	}

	private void RefreshReadValues()
	{
		IModelDoc2 doc;
		string configuration;
		try
		{
			doc = WatermarkPropertyAccess.ResolveDocument(_getSolidWorks(), _readSelectedPart.Checked, out configuration);
			_readStatus.Text = doc == null ? "打开模型后可预览；刷新不会保存设置。" : doc.GetTitle() + " / " + configuration + "；尚未保存设置";
		}
		catch (Exception ex)
		{
			_readStatus.Text = "读取失败：" + ex.Message;
			foreach (DataGridViewRow row in _grid.Rows) { row.Cells["ResolvedValue"].Value = ""; row.Cells["ResolvedValue"].ToolTipText = ""; }
			return;
		}
		foreach (DataGridViewRow row in _grid.Rows)
		{
			PropertyWatermarkField field = row.DataBoundItem as PropertyWatermarkField;
			if (field == null || doc == null) { row.Cells["ResolvedValue"].Value = ""; row.Cells["ResolvedValue"].ToolTipText = ""; continue; }
			try
			{
				WatermarkPropertyAccess.Value value = WatermarkPropertyAccess.Read(doc, configuration, field.Source, field.PropertyName);
				row.Cells["ResolvedValue"].Value = string.IsNullOrWhiteSpace(value.Text) ? "—" : value.Text;
				row.Cells["ResolvedValue"].ToolTipText = field.Source == "原生属性" ? "模型计算属性 / " + configuration : value.Origin;
			}
			catch (Exception ex) { row.Cells["ResolvedValue"].Value = "读取失败"; row.Cells["ResolvedValue"].ToolTipText = ex.Message; }
		}
	}

	private static List<string> PropertyNames(IModelDoc2 doc)
	{
		return PropertyIo.Names(doc);
	}

	private void Save()
	{
		try { ApplyConfig(); _refreshWatermark(); Close(); }
		catch (Exception ex) { _readStatus.Text = "保存失败：" + ex.Message; Logger.Error("Save watermark settings failed", ex); }
	}

	private void ApplyConfig()
	{
		if (!_grid.EndEdit()) throw new InvalidOperationException("请先完成当前字段的编辑。");
		((BindingSource)_grid.DataSource).EndEdit();
		if (Fields().Count == 0) throw new InvalidOperationException("请保留至少一个字段；暂不显示时可取消字段的启用勾选。");
		if (Fields().Count > 20 || Fields().Any(field => string.IsNullOrWhiteSpace(field.DisplayName) || string.IsNullOrWhiteSpace(field.PropertyName)))
			throw new InvalidOperationException("字段最多 20 个，显示名称和属性名称均不能为空。");
		_grid.EndEdit(); _config.PropertyWatermark.MoveEnabled = _moveEnabled.Checked; _config.PropertyWatermark.ReadSelectedPartProperties = _readSelectedPart.Checked; _config.PropertyWatermark.FontName = "SimSun"; _config.PropertyWatermark.FontSize = (float)_fontSize.Value; _config.PropertyWatermark.OpacityPercent = (int)_opacity.Value; _config.PropertyWatermark.DisplayScalePercent = (int)_displayScale.Value; _config.PropertyWatermark.Fields = Fields(); PropertyWatermarkConfig.Normalize(_config.PropertyWatermark);
		// Never mutate the live object returned by _load before persistence has
		// succeeded, including when the save callback reports a failure.
		PluginConfig latest = ConfigStore.Clone(_load());
		latest.PropertyWatermark = PropertyWatermarkConfig.Merge(_baseline, _config.PropertyWatermark, latest.PropertyWatermark);
		_save(latest);
		_config = new PluginConfig { PropertyWatermark = latest.PropertyWatermark.Clone() };
		LoadConfig();
		_baseline = _config.PropertyWatermark.Clone();
	}
}
