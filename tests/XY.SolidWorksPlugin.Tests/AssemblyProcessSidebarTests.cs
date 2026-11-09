using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class AssemblyProcessSidebarTests
{
    [Theory]
    [InlineData(380)]
    [InlineData(520)]
    public void EveryToolbarTabFitsWithoutHiddenButtons(int width) => Sta(() =>
    {
        using var fixture = new Fixture();
        fixture.ShowSidebar();
        fixture.Window.Width = width;
        var tabs = Assert.Single(Descendants(fixture.Window).OfType<TabControl>());
        foreach (TabPage page in tabs.TabPages)
        {
            tabs.SelectedTab = page;
            fixture.Window.PerformLayout(); Application.DoEvents();
            var flow = Assert.Single(page.Controls.OfType<FlowLayoutPanel>());
            foreach (Control control in flow.Controls)
                Assert.True(flow.ClientRectangle.Contains(control.Bounds), page.Text + ": " + control.Text + " clipped " + control.Bounds + " in " + flow.ClientRectangle);
            Assert.True(fixture.Grid.Height > 100, page.Text + " leaves no usable part list");
        }
    });

    [Fact]
    public void NarrowSidebarRetainsEveryExistingButtonAndVisibleWriteFooter() => Sta(() =>
    {
        using var fixture = new Fixture();
        var original = Descendants(fixture.Window).OfType<Button>().ToArray();
        fixture.ShowSidebar();
        foreach (var button in original)
        {
            Assert.False(button.IsDisposed);
            Assert.Contains(button, Descendants(fixture.Window));
        }
        Assert.True(fixture.Grid.Height > 100);
        foreach (var caption in new[] { "写入所选行", "写入所有可写行", "关闭" })
        {
            var button = Assert.Single(Descendants(fixture.Window).OfType<Button>(), b => b.Text == caption);
            Assert.True(button.Visible);
            Rectangle bounds = fixture.Window.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Assert.True(fixture.Window.ClientRectangle.Contains(bounds), caption + " clipped: " + bounds);
        }
    });

    [Fact]
    public void CheckedVisibleRowsOverrideHighlightAndHiddenChecksNeverJoinBatch() => Sta(() =>
    {
        using var fixture = new Fixture();
        fixture.AddPart("A", "激光下料-检验", true);
        fixture.AddPart("B", "钻孔-包装", false);
        fixture.AddPart("Hidden", "车削", true);
        fixture.ShowSidebar();
        fixture.Grid.CurrentCell = fixture.Grid.Rows[1].Cells["Part"];
        fixture.Grid.ClearSelection(); fixture.Grid.Rows[1].Selected = true;
        fixture.Grid.Rows[2].Visible = false;
        Assert.Equal(new[] { fixture.Grid.Rows[0] }, fixture.SelectedRows());
        fixture.Grid.Rows[0].Cells["Checked"].Value = false;
        Assert.Equal(new[] { fixture.Grid.Rows[1] }, fixture.SelectedRows());
    });

    [Fact]
    public void IndependentCombosAndAddRemoveOnlyChangeCurrentRowDraft() => Sta(() =>
    {
        using var fixture = new Fixture();
        fixture.AddPart("A", "激光下料-检验", false);
        fixture.AddPart("B", "钻孔-包装", false);
        fixture.ShowSidebar();
        fixture.Grid.CurrentCell = fixture.Grid.Rows[0].Cells["Process"];
        Field<CheckBox>(fixture.Window, "_independentSteps").Checked = true;
        var editor = Field<FlowLayoutPanel>(fixture.Window, "_stepEditor");
        var choices = Descendants(editor).OfType<ComboBox>().ToArray();
        Assert.Equal(new[] { "激光下料", "检验" }, choices.Select(c => c.Text));
        fixture.Calls.Clear();
        choices[0].Text = "自定义精加工";
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(choices[0], new object[] { EventArgs.Empty });
        Assert.Equal("检验", choices[1].Text);
        Assert.Equal("自定义精加工-检验", fixture.Grid.Rows[0].Cells["Process"].Value);
        Click(Descendants(editor).OfType<Button>().First(b => b.Text == "+"));
        Assert.Equal("自定义精加工-新工序-检验", fixture.Grid.Rows[0].Cells["Process"].Value);
        Click(Descendants(editor).OfType<Button>().Where(b => b.Text == "−").ElementAt(1));
        Assert.Equal("自定义精加工-检验", fixture.Grid.Rows[0].Cells["Process"].Value);
        Assert.Equal("钻孔-包装", fixture.Grid.Rows[1].Cells["Process"].Value);
        Assert.True((bool)fixture.Grid.Rows[0].Tag.GetType().GetProperty("ProcessEditedManually")!.GetValue(fixture.Grid.Rows[0].Tag)!);
        Assert.Empty(fixture.Calls);
        fixture.Grid.CurrentCell = fixture.Grid.Rows[1].Cells["Process"];
        Assert.Equal(new[] { "钻孔", "包装" }, Descendants(editor).OfType<ComboBox>().Select(c => c.Text));
    });

    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Sidebar test did not complete.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<string> Calls = new List<string>();
        internal readonly AssemblyProcessRecognitionWindow Window;
        internal DataGridView Grid => Field<DataGridView>(Window, "_grid");
        internal Fixture()
        {
            var configuration = Proxy<Configuration>(call => call.MethodName == "get_Name" ? "Default" : null);
            var manager = Proxy<ConfigurationManager>(call => call.MethodName == "get_ActiveConfiguration" ? configuration : null);
            var model = Proxy<IModelDoc2>(call => call.MethodName switch {
                "GetPathName" => "", "GetTitle" => "SidebarFixture", "GetUpdateStamp" => 1,
                "get_ConfigurationManager" => manager, "GetType" => 2, _ => throw new InvalidOperationException("Unexpected host call: " + call.MethodName)
            });
            Window = new AssemblyProcessRecognitionWindow(() => throw new InvalidOperationException("Unexpected SW access"), model, new ProcessRecognitionConfig { Separator = "-" }, () => { });
            typeof(AssemblyProcessRecognitionWindow).GetField("_queuePrepared", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, true);
        }
        internal void AddPart(string name, string sequence, bool check)
        {
            int index = Grid.Rows.Add(check, Grid.Rows.Count + 1, name, "Default", "钣金件", sequence, "待确认", "未标记外购");
            var type = typeof(AssemblyProcessRecognitionWindow).GetNestedType("BatchPart", BindingFlags.NonPublic)!;
            object part = Activator.CreateInstance(type, true)!;
            type.GetProperty("Name")!.SetValue(part, name);
            type.GetProperty("Configuration")!.SetValue(part, "Default");
            type.GetProperty("Row")!.SetValue(part, Grid.Rows[index]);
            Grid.Rows[index].Tag = part;
        }
        internal void ShowSidebar()
        {
            Window.PrepareForSidebar();
            Window.ShowInTaskbar = false; Window.StartPosition = FormStartPosition.Manual;
            Window.Location = new Point(-2000, -2000); Window.Size = new Size(380, 700);
            Window.Show(); Window.PerformLayout(); Application.DoEvents();
        }
        internal DataGridViewRow[] SelectedRows() => (DataGridViewRow[])typeof(AssemblyProcessRecognitionWindow).GetMethod("SelectedVisiblePartRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Window, null)!;
        private T Proxy<T>(Func<IMethodCallMessage, object?> invoke) => (T)new FakeCom(typeof(T), call => { Calls.Add(call.MethodName); return invoke(call); }).GetTransparentProxy();
        public void Dispose() => Window.Dispose();
    }
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _invoke;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> invoke) : base(type) { _invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { return new ReturnMessage(_invoke(call), call.Args, call.Args.Length, call.LogicalCallContext, call); }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
}
