using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace XY.SolidWorksPlugin;

internal sealed partial class SettingsWindow
{
	// Ring layout recommendations. Applying one re-spaces every ring evenly and
	// moves bindings that no longer fit into the unassigned pool.
	private void EditRingPreset()
	{
		ApplyRows();
		SceneMenuConfig scene = GetScene(_activeSceneKey);
		int[][] presets = WheelRingPresets.ForStyle(scene.StyleId);
		if (presets.Length == 0)
		{
			ToolNotice.Show(this, "当前样式没有推荐环设置，可切换到经典轮盘或图标轮盘后再调整。",
				DisplayName(), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		using Form dialog = new Form
		{
			Text = "环数量与每环格数",
			StartPosition = FormStartPosition.CenterParent,
			Font = Font,
			AutoScaleMode = AutoScaleMode.Dpi,
			ClientSize = new Size(400, 240),
			ShowInTaskbar = false,
			MinimizeBox = false,
			MaximizeBox = false
		};
		var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
		var choices = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
		foreach (int[] preset in presets)
		{
			choices.Items.Add(string.Join(" + ", preset.Select(slots => slots + " 格")) + "（" + preset.Length + " 环）");
		}
		int current = presets.ToList().FindIndex(preset =>
			preset.Length == scene.Rings.Count(ring => ring.Enabled)
			&& preset.Where((slots, index) => index < scene.Rings.Count && scene.Rings[index].Slots == slots).Count() == preset.Length);
		choices.SelectedIndex = current >= 0 ? current : Math.Min(presets.Length - 1, Math.Max(0, scene.Rings.Count(ring => ring.Enabled) - 2));
		var hint = new Label
		{
			Text = "确认后各环按推荐值等分直径；超出容量的绑定会移入“未分配”列表，不会被删除。",
			AutoSize = true,
			MaximumSize = new Size(340, 0),
			Margin = new Padding(3, 12, 3, 12)
		};
		var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		var ok = new Button { Text = "应用", DialogResult = DialogResult.OK, AutoSize = true };
		footer.Controls.Add(ok);
		footer.Controls.Add(new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true });
		body.Controls.Add(new Label { Text = "推荐设置", AutoSize = true, Margin = new Padding(3, 8, 12, 8) }, 0, 0);
		body.Controls.Add(choices, 0, 1);
		body.Controls.Add(hint, 0, 2);
		body.Controls.Add(footer, 0, 3);
		dialog.Controls.Add(body);
		dialog.AcceptButton = ok;
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		try
		{
			ApplyRingPreset(presets[Math.Max(0, choices.SelectedIndex)]);
		}
		catch (Exception ex)
		{
			ToolNotice.Show(this, ex.Message, "环设置未应用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private void ApplyRingPreset(int[] slots)
	{
		_ringSyncTimer.Stop();
		ApplyRows();
		WheelRingPresets.Apply(GetScene(_activeSceneKey), slots);
		// Reload the editor before RefreshPreview reads it back into the scene.
		LoadSceneRows();
	}
}
