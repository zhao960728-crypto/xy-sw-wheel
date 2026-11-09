using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[Collection("Command capture interaction")]
public sealed class SettingsRecordingIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingFromAnOrdinaryCellImportsAnExecutableComboOrCancelsWithoutChangingIt(bool cancel) => Sta(() =>
    {
        var current = ConfigStore.Deserialize("{}");
        ConfigStore.Normalize(current);
        current.Menus[SceneKeys.Blank].Rings[0].Items.Clear();
        current.Menus[SceneKeys.Blank].Rings[0].Items.Add(new MenuItemConfig
        {
            Slot = 0, Label = "原功能", Kind = ActionKinds.Hotkey, Value = "Ctrl+C", ComboAdvanceKey = "Shift"
        });
        var host = (ISldWorks)new EmptySolidWorks().GetTransparentProxy();
        using var settings = new SettingsWindow(() => ConfigStore.Clone(current), value => current = ConfigStore.Clone(value), getSolidWorks: () => host);
        Prepare(settings);
        var grid = Field<DataGridView>(settings, "_grid");
        grid.CurrentCell = grid.Rows[0].Cells["Label"];
        string originalKind = Convert.ToString(grid.Rows[0].Cells["Kind"].Value)!;
        string originalValue = Convert.ToString(grid.Rows[0].Cells["Value"].Value)!;
        typeof(SettingsWindow).GetMethod("RecordStepsForCurrentCell", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, null);
        using var recorder = Assert.Single(settings.OwnedForms.OfType<CommandRecorderWindow>());
        recorder.Opacity = 0;
        Assert.False(settings.Enabled);
        Assert.True(recorder.Enabled);
        Click(Button(recorder, "开始录制"));
        var session = Field<CommandRecordingSession>(recorder, "_session");
        session.Append(new ComboCommandConfig { Label = "新命令", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(1), RequireAdvanceKey = true });
        Application.DoEvents();
        Click(Button(recorder, cancel ? "放弃录制" : "结束并导入"));
        Application.DoEvents();
        Assert.True(settings.Enabled);
        var item = Assert.Single(Field<PluginConfig>(settings, "_config").Menus[SceneKeys.Blank].Rings[0].Items);
        if (cancel)
        {
            Assert.Equal(originalKind, grid.Rows[0].Cells["Kind"].Value);
            Assert.Equal(originalValue, grid.Rows[0].Cells["Value"].Value);
            Assert.Equal(ActionKinds.Hotkey, item.Kind);
            Assert.Equal("Ctrl+C", item.Value);
            Assert.Empty(item.ComboCommands);
        }
        else
        {
            Assert.Equal(ActionKinds.Combo, item.Kind);
            Assert.Equal(NativeCommandReference.Encode(1), Assert.Single(item.ComboCommands).Value);
            Assert.True(item.ComboAdvanceEnabled);
            Assert.Equal("Shift", item.ComboAdvanceKey);
        }
        Assert.Equal(ActionKinds.Hotkey, current.Menus[SceneKeys.Blank].Rings[0].Items[0].Kind);
        Assert.Equal("Ctrl+C", current.Menus[SceneKeys.Blank].Rings[0].Items[0].Value);
    });

    private sealed class EmptySolidWorks : RealProxy
    {
        public EmptySolidWorks() : base(typeof(ISldWorks)) { }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            return new ReturnMessage(null, call.Args, call.Args.Length, call.LogicalCallContext, call);
        }
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));
    private static Button Button(Control parent, string caption) => Descendants(parent).OfType<Button>().Single(button => button.Text == caption);
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
    private static void Prepare(Form window)
    {
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new Point(-20000, -20000);
        window.Show();
        Application.DoEvents();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
