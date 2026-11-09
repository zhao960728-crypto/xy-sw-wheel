using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace XY.SolidWorksPlugin;

internal static class ProcessSidebarLayout
{
    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    internal static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(3), Margin = Padding.Empty };
        panel.Controls.AddRange(controls.Where(c => c != null).ToArray()); return panel;
    }
    internal static void Tab(TabControl tabs, string title, params Control[] controls)
    {
        var page = new TabPage(title) { Padding = new Padding(2) }; page.Controls.Add(Flow(controls)); tabs.TabPages.Add(page);
    }
    internal static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    internal static void AutoHeightTabs(TableLayoutPanel root, int row, TabControl tabs)
    {
        bool arranging = false;
        void Arrange()
        {
            if (arranging || root.IsDisposed || tabs.IsDisposed || tabs.Width < 180) return;
            var flow = tabs.SelectedTab?.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (flow == null || flow.ClientSize.Width < 100) return;
            arranging = true;
            try
            {
                int available = Math.Max(80, flow.ClientSize.Width - flow.Padding.Horizontal);
                foreach (Control control in flow.Controls)
                {
                    int width = Math.Max(60, available - control.Margin.Horizontal);
                    if (control.AutoSize) control.MaximumSize = new Size(width, 0);
                    else if (control.Width > width) control.Width = width;
                }
                flow.PerformLayout();
                int extent = flow.Controls.Cast<Control>().Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max() + flow.Padding.Bottom;
                int chrome = Math.Max(tabs.ItemSize.Height + 8, tabs.Height - flow.Height);
                float height = Math.Max(100, extent + chrome + tabs.Margin.Vertical + 2);
                if (Math.Abs(root.RowStyles[row].Height - height) > 1) root.RowStyles[row].Height = height;
            }
            finally { arranging = false; }
        }
        foreach (TabPage page in tabs.TabPages)
        {
            var flow = page.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (flow == null) continue;
            flow.AutoScroll = false;
            flow.Layout += (_, __) => Arrange();
        }
        tabs.SelectedIndexChanged += (_, __) => Arrange();
        tabs.SizeChanged += (_, __) => Arrange();
        tabs.VisibleChanged += (_, __) => Arrange();
        root.SizeChanged += (_, __) => Arrange();
        Arrange();
    }
    internal static string[] Choices(ProcessRecognitionConfig config)
    {
        return new[] { "激光下料", "冲床", "折弯", "压铆", "焊接", "去毛刺", "打磨", "攻丝", "沉孔", "钻孔", "机加工", "检验", "包装", "出货" }
            .Concat(ProcessRecognitionConfig.ParseProcessNameMappings(config.ProcessNameMappings).Select(pair => pair.Value))
            .Concat(ProcessRecognitionConfig.ParseCombinations(config.ProcessCombinations).SelectMany(pair => ProcessSequenceEditorWindow.Split(pair.Value, config.Separator)))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    internal static void Wrap(Control parent, params Label[] labels)
    {
        void Resize() { int width = Math.Max(100, parent.ClientSize.Width - 20); foreach (var label in labels) if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0); }
        parent.SizeChanged += (_, __) => Resize(); Resize();
    }
}
