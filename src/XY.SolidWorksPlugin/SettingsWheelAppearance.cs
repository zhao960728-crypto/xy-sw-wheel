using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed partial class SettingsWindow
{
    private readonly ToolTip _wheelPreviewTip = new ToolTip { InitialDelay = 400, AutoPopDelay = 10000 };

    private Control BuildWheelAppearanceTools()
    {
        _preview.HoverTextChanged += text => _wheelPreviewTip.SetToolTip(_preview, text);
        Disposed += (_, _) => _wheelPreviewTip.Dispose();
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 8) };
        panel.Controls.Add(MakeButton("样式 / 图文 / 配色", EditWheelAppearance, 170));
        panel.Controls.Add(MakeButton("环数量与格数", EditRingPreset, 118));
        panel.Controls.Add(MakeButton("自动匹配图标", AutoMatchIcons, 118));
        panel.Controls.Add(MakeButton("自由布局", EditFreeLayout, 98));
        panel.Controls.Add(MakeButton("文字规则 / 批量设置", EditWheelTextRules, 170));
        panel.Controls.Add(MakeButton("当前格图标库", PickWheelLibraryIcon, 125));
        panel.Controls.Add(MakeButton("自选图片", PickWheelImage, 90));
        panel.Controls.Add(MakeButton("恢复自动图标", ResetWheelIcon, 120));
        panel.Controls.Add(new Label { Text = "预览中按住格子后拖动换位置；空格可接收命令。", AutoSize = true, MaximumSize = new Size(380, 0) });
        return panel;
    }

    // Icon library hand-off: match artwork to the command each cell already
    // holds. An explicit user image is never replaced, and only the icon id
    // changes, so the bound command identity stays exactly as configured.
    // Icons rendered for the command library are shared by id, so listing a few
    // thousand commands does not allocate a bitmap per row.
    private static readonly Dictionary<string, Bitmap> CatalogIconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _nativeCatalogIconCache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
    private bool _nativeCatalogCleanupAttached;

    internal string CatalogIconId(KeyboardShortcutRow row)
    {
        string display = PreferredCommandAlias(row);
        string command = row?.Command ?? "";
        return WheelIconCatalog.TryGetSemanticIconId(command, string.IsNullOrWhiteSpace(display) ? command : display, "", row?.Category ?? "", out string id)
            ? id : "toolbox";
    }

    private Bitmap CatalogIconBitmap(KeyboardShortcutRow row)
    {
        int iconSize = Math.Max(24, Math.Min(128, (int)Math.Round(24d * DeviceDpi / 96)));
        if (ShortcutPaletteMatcher.TryGetAction(row, out string kind, out string value))
        {
            string key = _activeSceneKey + "|" + kind + "|" + value + "|" + iconSize;
            if (_nativeCatalogIconCache.TryGetValue(key, out Bitmap nativeCached)) return nativeCached;
            var native = new Bitmap(iconSize, iconSize);
            using (var graphics = Graphics.FromImage(native))
            {
                if (RadialCommandIcons.TryDrawExactNative(graphics, new MenuItemConfig { Kind = kind, Value = value },
                    new RectangleF(0, 0, iconSize, iconSize), _activeSceneKey))
                {
                    if (!_nativeCatalogCleanupAttached)
                    {
                        _nativeCatalogCleanupAttached = true;
                        Disposed += (_, _) => { foreach (Bitmap image in _nativeCatalogIconCache.Values) image.Dispose(); _nativeCatalogIconCache.Clear(); };
                    }
                    _nativeCatalogIconCache[key] = native;
                    return native;
                }
            }
            native.Dispose();
        }
        string id = CatalogIconId(row);
        string catalogueKey = id + "|" + iconSize;
        if (CatalogIconCache.TryGetValue(catalogueKey, out Bitmap cached)) return cached;
        Bitmap bitmap;
        try { bitmap = WheelIconCatalog.Render(id, iconSize); }
        catch { return CatalogIconCache.TryGetValue("toolbox|" + iconSize, out Bitmap fallback) ? fallback : null; }
        CatalogIconCache[catalogueKey] = bitmap;
        return bitmap;
    }

    private void AutoMatchIcons()
    {
        ApplyRows();
        SceneMenuConfig scene = GetScene(_activeSceneKey);
        int matched = 0, kept = 0, missed = 0;
        foreach (RingConfig ring in scene.Rings.Where(r => r.Enabled))
        {
            foreach (MenuItemConfig item in ring.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.IconPath)) { kept++; continue; }
				if ((item.Kind == ActionKinds.NativeCommand && NativeCommandReference.TryDecodeId(item.Value, out _))
					|| (item.Kind == ActionKinds.Basic && NativeWheelIconMappings.TryGetBasicCommandId(item.Value, out _)))
				{
					item.IconId = ""; item.IconData = "";
					matched++;
					continue;
				}
				if (item.Kind == ActionKinds.Basic
					&& WheelIconCatalog.TryGetBasicActionIconId(item.Value, out string basicIconId))
				{
					item.IconId = basicIconId;
					matched++;
					continue;
				}
                string command = IconCommandIdentity(item);
                if (WheelIconCatalog.TryGetSemanticIconId(command, item.Label, "", item.Kind, out string id))
                {
                    item.IconId = id;
                    matched++;
                }
                else missed++;
            }
        }
        LoadSceneRows();
        ToolNotice.Show(this,
            "已匹配 " + matched + " 个格子（原生命令优先采用原生图标）；保留自选图片 " + kept + " 个；未命中 " + missed + " 个。",
            DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string IconCommandIdentity(MenuItemConfig item)
    {
        if (item == null) return "";
        if (item.Kind.Equals(ActionKinds.QinJian, StringComparison.OrdinalIgnoreCase)
            && QinJianActionValue.TryParse(item.Value, out _, out string qinJianName)) return qinJianName;
        if (item.Kind.Equals(ActionKinds.ExternalPlugin, StringComparison.OrdinalIgnoreCase))
        {
            if (ExternalPluginActionValue.TryParse(item.Value, out _, out string externalName)) return externalName;
            if (ExternalNamedCommandReference.TryDecode(item.Value, out _, out _, out string named)) return named;
            if (ExternalToolbarCommandReference.TryDecode(item.Value, out _, out _)) return ExternalToolbarCommandReference.Name(item.Value);
        }
        return item.Kind.Equals(ActionKinds.NativeCommand, StringComparison.OrdinalIgnoreCase) ? "" : (item.Value ?? "");
    }

    // Free layout keeps every binding on its ring slot and only moves where the
    // cell is drawn, so switching styles or tidying never loses a command.
    private void EditFreeLayout()
    {
        ApplyRows();
        SceneMenuConfig target = GetScene(_activeSceneKey);
        SceneMenuConfig scene = CloneAppearanceScene(target);
        using Form dialog = new Form
        {
            Text = "自由布局",
            StartPosition = FormStartPosition.CenterParent,
            Font = Font,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(400, 250),
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 4 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var columns = new NumericUpDown { Minimum = 1, Maximum = 12, Value = 4, Dock = DockStyle.Fill };
        var useFree = new CheckBox { Text = "使用自由布局（格子按坐标摆放）", AutoSize = true, Checked = scene.StyleId == FreeWheelLayout.StyleId };
        body.Controls.Add(new Label { Text = "每行格数", AutoSize = true, Margin = new Padding(3, 8, 12, 8) }, 0, 0);
        body.Controls.Add(columns, 1, 0);
        body.Controls.Add(useFree, 0, 1); body.SetColumnSpan(useFree, 2);
        body.Controls.Add(new Label
        {
            Text = "整理会按网格摆放已绑定的格子；清除则恢复按环排布。两者都不改动命令绑定。",
            AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(3, 10, 3, 6)
        }, 0, 2);
        body.SetColumnSpan(body.Controls[body.Controls.Count - 1], 2);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.Add(MakeButton("整理", () =>
        {
            int placed = FreeWheelLayout.Tidy(scene, (int)columns.Value);
            scene.StyleId = FreeWheelLayout.StyleId;
            useFree.Checked = true;
            ToolNotice.Show(this, "已摆放 " + placed + " 个格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, 80));
        footer.Controls.Add(MakeButton("清除", () =>
        {
            FreeWheelLayout.Clear(scene);
            useFree.Checked = false;
            ToolNotice.Show(this, "已恢复按环排布。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, 80));
        var apply = new Button { Text = "应用", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        footer.Controls.Add(apply);
        footer.Controls.Add(cancel);
        body.Controls.Add(footer, 0, 3); body.SetColumnSpan(footer, 2);
        dialog.Controls.Add(body);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (useFree.Checked) scene.StyleId = FreeWheelLayout.StyleId;
        else if (scene.StyleId == FreeWheelLayout.StyleId) scene.StyleId = "01";
        target.StyleId = scene.StyleId;
        target.Rings = scene.Rings;
        LoadSceneRows();
    }

    // The appearance dialog is the single place where the wheel's look is set.
    // Every control here maps onto a field the renderer actually reads, so a
    // colour picked in this dialog is the colour the wheel paints.
    private void EditWheelAppearance()
    {
        ApplyRows();
        var scene = GetScene(_activeSceneKey);
        WheelAppearance appearance = scene.Appearance == null ? WheelAppearance.DarkGlass() : scene.Appearance.Clone();
        appearance.Normalize();
        var draftScene = CloneAppearanceScene(scene);
        bool syncingAppearance = false;

        using var dialog = new Form { Text = "轮盘样式 · 配色 · 排版", StartPosition = FormStartPosition.CenterParent,
            Font = Font, AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(920, 660), MinimumSize = new Size(680, 460), ShowInTaskbar = false,
            MinimizeBox = false, MaximizeBox = false };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 16, 0) };
        var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int cursor = 0;
        void Row(string label, params Control[] controls)
        {
            body.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 9, 14, 8), Anchor = AnchorStyles.Left }, 0, cursor);
            var host = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
            foreach (Control control in controls) host.Controls.Add(control);
            body.Controls.Add(host, 1, cursor);
            cursor++;
        }
        void Span(Control control, int height = 0)
        {
            if (height > 0) control.Height = height;
            control.Margin = new Padding(3, 10, 3, 10);
            body.Controls.Add(control, 0, cursor); body.SetColumnSpan(control, 2);
            cursor++;
        }
        ComboBox choices(string[] labels, int selected, int width = 150)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            box.Items.AddRange(labels); box.SelectedIndex = Math.Max(0, selected); return box;
        }
        NumericUpDown number(int min, int max, int value, int width = 76)
        {
            var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = width }; return box;
        }
        // Set once the preview panel exists; every colour change repaints it.
        Action syncPreview = () => { };

        // A swatch button shows the live colour and opens the system picker.
        Button swatch(string caption, Func<Color> read, Action<Color> write)
        {
            var button = new Button { Text = caption, Width = 104, Height = 28, FlatStyle = FlatStyle.Flat,
                BackColor = read(), ForeColor = ReadableInk(read()), Margin = new Padding(3, 2, 3, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 213, 221);
            button.Click += delegate
            {
                using var picker = new ColorDialog { Color = read(), FullOpen = true, AnyColor = true };
                if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                write(picker.Color);
				appearance.PresetId = WheelAppearance.PresetCustom;
				appearance.ExplicitPreset = true;
                SyncPreset();
            };
            return button;
        }

        var styleChoices = WheelLayoutStyles.All.ToList();
        string currentStyle = WheelLayoutStyles.NormalizeStyleId(scene.StyleId);
        if (!styleChoices.Any(choice => choice.Id == currentStyle)) styleChoices.Add(WheelLayoutStyles.Find(currentStyle));
        string[] styles = styleChoices.Select(choice => choice.Id).ToArray();
        string[] modes = { "Auto", "IconOnly", "TextOnly", "IconAndText" };
        string[] presetIds = { WheelAppearance.PresetDarkGlass, WheelAppearance.PresetClassic, WheelAppearance.PresetNative, WheelAppearance.PresetMidnight, WheelAppearance.PresetCustom };
        string[] shapes = { "Circle", "Rounded", "Square" };

        var style = choices(styleChoices.Select(choice => choice.Name).ToArray(), Array.IndexOf(styles, currentStyle));
        style.Name = "WheelLayoutStyle";
        var styleDescription = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.FromArgb(78, 94, 108), Text = styleChoices[style.SelectedIndex].Description };
        var content = choices(new[] { "自动图文", "仅图标", "仅文字", "图标与文字" }, Array.IndexOf(modes, scene.ContentMode));
        var iconColor = choices(new[] { "绿色", "紫色" }, scene.IconColor == "Purple" ? 1 : 0, 110);
        var preset = choices(new[] { "深色玻璃", "经典浅色", "原生灰蓝", "午夜蓝", "自定义" }, Math.Max(0, Array.IndexOf(presetIds, appearance.PresetId)));
        var surfaceOpacity = number(5, 100, scene.SurfaceOpacityPercent > 0 ? scene.SurfaceOpacityPercent : 88);
        var glass = new CheckBox { Text = "玻璃高光", Checked = appearance.Glass, AutoSize = true, Margin = new Padding(3, 8, 3, 8) };
        var shadow = number(0, 100, appearance.ShadowPercent);
        var fill = choices(new[] { "纯色", "渐变" }, string.Equals(appearance.FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 110);
        var angle = number(0, 359, appearance.GradientAngle);
        var autoText = new CheckBox { Text = "文字色跟随盘面", Checked = string.IsNullOrWhiteSpace(appearance.TextColor), AutoSize = true, Margin = new Padding(3, 8, 3, 8) };

        // Declared up front: the swatch click handler refreshes every swatch, and
        // a local function may only capture variables already assigned.
        var swatches = new Button[6];
        swatches[0] = swatch("起始色", () => WheelAppearance.ParseColor(appearance.StartColor, Color.FromArgb(42, 56, 70)), value => appearance.StartColor = WheelAppearance.ToHex(value));
        swatches[1] = swatch("结束色", () => WheelAppearance.ParseColor(appearance.EndColor, Color.FromArgb(19, 27, 36)), value => appearance.EndColor = WheelAppearance.ToHex(value));
        swatches[2] = swatch("边框色", () => appearance.GetBorderColor(), value => appearance.BorderColor = WheelAppearance.ToHex(value));
        swatches[3] = swatch("悬停色", () => appearance.GetHoverColor(), value => appearance.HoverColor = WheelAppearance.ToHex(value));
        swatches[4] = swatch("选中色", () => appearance.GetSelectedColor(), value => appearance.SelectedColor = WheelAppearance.ToHex(value));
        swatches[5] = swatch("文字色", () => appearance.GetTextColor(), value => appearance.TextColor = WheelAppearance.ToHex(value));

        var row = FindSelectedCommandRow();
        var state = row == null ? null : ReadComboRowState(row.Tag);
        var cellContent = choices(new[] { "跟随当前轮盘", "自动图文", "仅图标", "仅文字", "图标与文字" }, state == null ? 0 : Array.IndexOf(modes, state.ContentMode) + 1);
        cellContent.Enabled = row != null;
        var shape = choices(new[] { "保留各格形状", "圆形", "圆角方", "方形" }, 0, 130);

        // Keeps every colour control in step with the appearance object, which
        // is what the preset list and the pickers both write into.
        void SyncAppearance()
        {
            syncingAppearance = true;
            try
            {
                swatches[0].BackColor = WheelAppearance.ParseColor(appearance.StartColor, Color.FromArgb(42, 56, 70));
                swatches[1].BackColor = WheelAppearance.ParseColor(appearance.EndColor, Color.FromArgb(19, 27, 36));
                swatches[2].BackColor = appearance.GetBorderColor();
                swatches[3].BackColor = appearance.GetHoverColor();
                swatches[4].BackColor = appearance.GetSelectedColor();
                swatches[5].BackColor = appearance.GetTextColor();
                foreach (Button button in swatches) button.ForeColor = ReadableInk(button.BackColor);
                autoText.Checked = string.IsNullOrWhiteSpace(appearance.TextColor);
                swatches[5].Enabled = !autoText.Checked;
                fill.SelectedIndex = string.Equals(appearance.FillMode, "Gradient", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                angle.Value = Math.Max(0, Math.Min(359, appearance.GradientAngle));
                glass.Checked = appearance.Glass;
                shadow.Value = Math.Max(0, Math.Min(100, appearance.ShadowPercent));
                swatches[1].Enabled = angle.Enabled = fill.SelectedIndex == 1;
                }
            finally { syncingAppearance = false; }
            syncPreview();
        }
        void SyncPreset()
        {
            syncingAppearance = true;
            try { preset.SelectedIndex = Math.Max(0, Array.IndexOf(presetIds, appearance.PresetId)); }
            finally { syncingAppearance = false; }
            SyncAppearance();
        }
        void Customize(Action change)
        {
            if (syncingAppearance) return;
            change();
            appearance.PresetId = WheelAppearance.PresetCustom;
            appearance.ExplicitPreset = true;
            SyncPreset();
        }
        preset.SelectedIndexChanged += delegate
        {
            if (syncingAppearance) return;
            if (preset.SelectedIndex < 0) return;
            if (preset.SelectedIndex == presetIds.Length - 1)
            {
                appearance.PresetId = WheelAppearance.PresetCustom;
                appearance.ExplicitPreset = true;
                syncPreview();
                return;
            }
            appearance = WheelAppearance.FromPreset(presetIds[preset.SelectedIndex]);
            SyncAppearance();
        };
        fill.SelectedIndexChanged += delegate { Customize(() => appearance.FillMode = fill.SelectedIndex == 1 ? "Gradient" : "Solid"); };
        angle.ValueChanged += delegate { Customize(() => appearance.GradientAngle = (int)angle.Value); };
        glass.CheckedChanged += delegate { Customize(() => appearance.Glass = glass.Checked); };
        shadow.ValueChanged += delegate { Customize(() => appearance.ShadowPercent = (int)shadow.Value); };
        autoText.CheckedChanged += delegate { Customize(() => appearance.TextColor = autoText.Checked ? "" : WheelAppearance.ToHex(appearance.GetTextColor())); };

        Row("轮盘样式", style);
        Span(styleDescription);
        Row("整页图文", content);
        Row("图标颜色", iconColor);
        Row("当前格图文", cellContent);
        Row("配色预设", preset);
        Row("盘面不透明 %", surfaceOpacity);
        Row("玻璃与投影", glass, shadow);
        Row("盘面填充", fill, angle);
        Row("盘面配色", swatches[0], swatches[1], swatches[2], swatches[3], swatches[4], swatches[5], autoText);
        Row("格子形状", shape);
        Span(new Label { Text = "配色与形状只影响外观；命令绑定、组合衔接和原生命令身份保持不变。", AutoSize = true,
            MaximumSize = new Size(480, 0), ForeColor = Color.FromArgb(78, 94, 108) });

        // Left column scrolls the controls, right column previews them live,
        // the same split the CAD palette dialog uses.
        var previewHost = new RadialMenuControl(Color.FromArgb(244, 246, 248))
        {
            Dock = DockStyle.Fill, MinimumSize = new Size(210, 190),
            ScaleMenuForPreview = true, BackColor = Color.FromArgb(244, 246, 248)
        };
        syncPreview = () =>
        {
            draftScene.StyleId = styles[style.SelectedIndex];
            draftScene.ContentMode = modes[content.SelectedIndex];
            draftScene.IconColor = iconColor.SelectedIndex == 1 ? "Purple" : "Green";
            draftScene.SurfaceOpacityPercent = (int)surfaceOpacity.Value;
            draftScene.Appearance = appearance.Clone();
            foreach (RingConfig draftRing in draftScene.Rings)
                foreach (MenuItemConfig draftItem in draftRing.Items)
                {
                    var original = scene.Rings.FirstOrDefault(r => r.Index == draftRing.Index)?.Items.FirstOrDefault(i => i.Slot == draftItem.Slot);
                    draftItem.CellShape = shape.SelectedIndex == 0 ? original?.CellShape ?? "Circle" : shapes[shape.SelectedIndex - 1];
                }
            if (row != null && TryReadGridRingSlot(row, out int ringIndex, out int slotIndex))
            {
                var item = FreeWheelLayout.Find(draftScene, ringIndex - 1, slotIndex - 1);
                if (item != null) item.ContentMode = cellContent.SelectedIndex == 0 ? "" : modes[cellContent.SelectedIndex - 1];
            }
            previewHost.SetMenu(_config, draftScene);
        };
        foreach (ComboBox control in new[] { style, content, iconColor, cellContent, shape })
            control.SelectedIndexChanged += (_, _) => syncPreview();
        void UpdateStyleControls()
        {
            WheelLayoutStyle choice = styleChoices[style.SelectedIndex];
            styleDescription.Text = choice.Description;
            shape.Enabled = choice.SupportsCellShapes;
            if (!shape.Enabled) shape.SelectedIndex = 0;
        }
        style.SelectedIndexChanged += (_, _) => UpdateStyleControls();
        UpdateStyleControls();
        surfaceOpacity.ValueChanged += (_, _) => syncPreview();

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 4) };
        var ok = new Button { Text = "应用", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        footer.Controls.Add(ok); footer.Controls.Add(cancel);
        var reset = new Button { Text = "恢复默认", AutoSize = true };
        reset.Click += delegate
        {
            appearance = new WheelAppearance();
            appearance.Normalize();
            SyncPreset();
            previewHost.Invalidate();
        };
        footer.Controls.Add(reset);
        footer.Dock = DockStyle.Bottom;
        footer.Padding = new Padding(12, 8, 12, 8);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16) };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        scroll.Controls.Add(body);
        outer.Controls.Add(scroll, 0, 0);
        outer.Controls.Add(previewHost, 1, 0);
        dialog.Controls.Add(outer); dialog.Controls.Add(footer); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        dialog.Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            dialog.MinimumSize = new Size(Math.Min(dialog.MinimumSize.Width, area.Width), Math.Min(dialog.MinimumSize.Height, area.Height));
            dialog.Size = new Size(Math.Min(dialog.Width, area.Width), Math.Min(dialog.Height, area.Height));
        };
        SyncAppearance();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        scene.StyleId = styles[style.SelectedIndex];
        scene.ContentMode = modes[content.SelectedIndex];
        scene.IconColor = iconColor.SelectedIndex == 1 ? "Purple" : "Green";
        scene.SurfaceOpacityPercent = (int)surfaceOpacity.Value;
        appearance.Normalize();
        scene.Appearance = appearance.Clone();
        if (shape.SelectedIndex > 0)
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;
                var cellState = ReadComboRowState(gridRow.Tag);
                cellState.CellShape = shapes[shape.SelectedIndex - 1];
                gridRow.Tag = cellState;
            }
        if (row != null)
        {
            state = ReadComboRowState(row.Tag);
            state.ContentMode = cellContent.SelectedIndex == 0 ? "" : modes[cellContent.SelectedIndex - 1];
            row.Tag = state;
        }
        RefreshPreview();
    }

    private static SceneMenuConfig CloneAppearanceScene(SceneMenuConfig scene)
    {
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        return serializer.Deserialize<SceneMenuConfig>(serializer.Serialize(scene));
    }

    private static Color ReadableInk(Color background) =>
        (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) >= 140
            ? Color.FromArgb(35, 45, 55) : Color.FromArgb(240, 246, 250);

    private void PickWheelLibraryIcon()
    {
        var row = FindSelectedCommandRow();
        if (!IsOccupiedCommandRow(row)) { ToolNotice.Show(this, "请先选择已设置命令的格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new WheelIconLibraryDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var state = ReadComboRowState(row.Tag); state.IconId = dialog.SelectedIconId; state.IconPath = "";
        state.IconData = WheelIconPreference.ExplicitCatalogueData(state.IconId); row.Tag = state;
        RefreshPreview();
    }

    private void PickWheelImage()
    {
        var row = FindSelectedCommandRow();
        if (!IsOccupiedCommandRow(row)) { ToolNotice.Show(this, "请先选择已设置命令的格子。", DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new OpenFileDialog { Title = "选择轮盘图标", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try {
            using var source = Image.FromFile(dialog.FileName);
            if (source.Width > 12000 || source.Height > 12000) throw new InvalidOperationException("图片尺寸过大，请选择 12000 像素以内的图片。");
            Directory.CreateDirectory(ConfigStore.AssetDirectory);
            var path = Path.Combine(ConfigStore.AssetDirectory, "wheel-icon-" + Guid.NewGuid().ToString("N") + ".png");
            using var icon = new Bitmap(128, 128);
            using (var graphics = Graphics.FromImage(icon)) {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float ratio = Math.Min(128f / source.Width, 128f / source.Height);
                graphics.DrawImage(source, (128 - source.Width * ratio) / 2, (128 - source.Height * ratio) / 2, source.Width * ratio, source.Height * ratio);
            }
            icon.Save(path, ImageFormat.Png);
            var state = ReadComboRowState(row.Tag); state.IconId = ""; state.IconPath = path; row.Tag = state;
            RefreshPreview();
        } catch (Exception ex) { ToolNotice.Show(this, ex.Message, "图标导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void SwapWheelCells(object sender, RadialCellsSwappedEventArgs e)
    {
        var source = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => TryReadGridRingSlot(r, out var ring, out var slot) && ring == e.SourceRingIndex + 1 && slot == e.SourceSlot + 1);
        var target = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => TryReadGridRingSlot(r, out var ring, out var slot) && ring == e.TargetRingIndex + 1 && slot == e.TargetSlot + 1);
        if (source == null || target == null || ReferenceEquals(source, target)) return;
        CommitGridEdits();
        _loadingScene = true;
        try {
            foreach (DataGridViewColumn column in _grid.Columns) {
                if (column.Name == "Ring" || column.Name == "Slot") continue;
                var value = source.Cells[column.Index].Value;
                source.Cells[column.Index].Value = target.Cells[column.Index].Value;
                target.Cells[column.Index].Value = value;
            }
            var tag = source.Tag; source.Tag = target.Tag; target.Tag = tag;
        } finally { _loadingScene = false; }
        SelectCommandCell(e.TargetRingIndex + 1, e.TargetSlot + 1);
        RefreshPreview();
        // The swap is otherwise silent: the wheel redraws and the grid scrolls,
        // but nothing says what moved where.
        Hint(e.SourceRingIndex == e.TargetRingIndex
            ? $"已交换第 {e.SourceRingIndex + 1} 圈的第 {e.SourceSlot + 1} 格与第 {e.TargetSlot + 1} 格"
            : $"已交换第 {e.SourceRingIndex + 1} 圈第 {e.SourceSlot + 1} 格与第 {e.TargetRingIndex + 1} 圈第 {e.TargetSlot + 1} 格");
    }

    private void ResetWheelIcon()
    {
        var row = FindSelectedCommandRow(); if (row == null) return;
        var state = ReadComboRowState(row.Tag); state.IconId = ""; state.IconData = ""; state.IconPath = ""; row.Tag = state; RefreshPreview();
    }
}
