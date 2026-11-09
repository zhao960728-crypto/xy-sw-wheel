using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

[CollectionDefinition("Command capture interaction", DisableParallelization = true)]
public sealed class CommandCaptureInteractionCollection { }

[Collection("Command capture interaction")]
public sealed class CommandCaptureInteractionTests
{
    [Fact]
    public void RecorderRequiresStartAndSupportsPauseAndContinue() => Sta(() =>
    {
        using var window = new CommandRecorderWindow();
        Prepare(window);
        var session = Field<CommandRecordingSession>(window, "_session");
        var toggle = Field<Button>(window, "_pause");
        Assert.Equal("开始录制", toggle.Text);
        Assert.True(session.Paused);
        Assert.Null(Active());
        session.Append(Command(1));
        Assert.Empty(session.Commands);
        Click(toggle);
        Assert.Same(session, Active());
        Assert.False(session.Paused);
        Assert.Equal("暂停录制", toggle.Text);
        session.Append(Command(1));
        Click(toggle);
        Assert.True(session.Paused);
        Assert.Equal("继续录制", toggle.Text);
        session.Append(Command(2));
        Assert.Single(session.Commands);
        Click(toggle);
        session.Append(Command(3));
        Application.DoEvents();
        Assert.Equal(new[] { NativeCommandReference.Encode(1), NativeCommandReference.Encode(3) }, session.Commands.Select(c => c.Value));
        Click(Button(window, "放弃录制"));
        Assert.Equal(DialogResult.Cancel, window.DialogResult);
        Assert.Null(Active());
    });

    [Fact]
    public void RecorderEditsAnExistingDraftAndCancelDoesNotOverwriteIt() => Sta(() =>
    {
        var source = new[] { Command(1), Command(2) };
        using var window = new CommandRecorderWindow(source);
        Prepare(window);
        var session = Field<CommandRecordingSession>(window, "_session");
        Assert.Equal(2, session.Commands.Count);
        var list = Field<ListView>(window, "_list");
        list.Items[0].Selected = true;
        Click(Button(window, "重录此步"));
        Assert.True(session.IsReplacing);
        Click(Button(window, "取消重录"));
        Assert.True(session.Paused);
        Assert.Equal(source[0].Value, session.Commands[0].Value);
        Click(Button(window, "重录此步"));
        session.Append(Command(3));
        session.Append(Command(4));
        Assert.True(session.Paused);
        Application.DoEvents();
        Assert.Equal(new[] { NativeCommandReference.Encode(3), source[1].Value }, session.Commands.Select(c => c.Value));
        Click(Button(window, "放弃录制"));
        Assert.Equal(NativeCommandReference.Encode(1), source[0].Value);
        Assert.Empty(window.Recorded);
        Assert.Null(Active());
    });

    [Fact]
    public void RecorderMoveKeepsTheMovedStepSelectedAndFinishClonesTheDraft() => Sta(() =>
    {
        using var window = new CommandRecorderWindow(new[] { Command(1), Command(2) });
        Prepare(window);
        var list = Field<ListView>(window, "_list");
        list.Items[1].Selected = true;
        Click(Button(window, "上移"));
        Application.DoEvents();
        Assert.Equal(0, list.SelectedIndex());
        Assert.Equal(NativeCommandReference.Encode(2), Field<CommandRecordingSession>(window, "_session").Commands[0].Value);
        Click(Button(window, "结束并导入"));
        Assert.Equal(DialogResult.OK, window.DialogResult);
        Assert.Equal(2, window.Recorded.Count);
        Assert.Null(Active());
    });

    [Fact]
    public void StepPropertiesAreTransactionalAndDelayEditsUpdateTheExecutedValue() => Sta(() =>
    {
        var source = new ComboCommandConfig { Label = "等待", Kind = ActionKinds.Delay, Value = "800", DelayMs = 800, WaitMode = ComboWaitModes.Delay };
        Type dialogType = typeof(CommandRecorderWindow).GetNestedType("StepPropertiesDialog", BindingFlags.NonPublic)!;
        using (var dialog = (Form)Activator.CreateInstance(dialogType, new object[] { source })!)
        {
            Prepare(dialog);
            Assert.Contains(Descendants(dialog).OfType<TextBox>(), box => box.ReadOnly && box.Text.Contains("800"));
            Field<TextBox>(dialog, "_label").Text = "取消的改名";
            Field<NumericUpDown>(dialog, "_delay").Value = 1200;
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
            Assert.Equal("等待", source.Label);
            Assert.Equal("800", source.Value);
        }
        using (var dialog = (Form)Activator.CreateInstance(dialogType, new object[] { source })!)
        {
            Prepare(dialog);
            Field<TextBox>(dialog, "_label").Text = "等待 1.2 秒";
            Field<NumericUpDown>(dialog, "_delay").Value = 1200;
            foreach (Button button in Descendants(dialog).OfType<Button>())
            {
                Rectangle bounds = dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                Assert.True(dialog.ClientRectangle.Contains(bounds), button.Text + " is clipped");
            }
            string? capture = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                Directory.CreateDirectory(capture);
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(capture, "recorded-step-properties.png"));
            }
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
            Assert.Equal("等待 1.2 秒", source.Label);
            Assert.Equal("1200", source.Value);
            Assert.Equal(1200, source.DelayMs);
        }
    });

    [Fact]
    public void SingleCaptureStopsBeforeTheUiProcessesTheFirstCommand() => Sta(() =>
    {
        using var window = new CommandCaptureWindow();
        Prepare(window);
        var session = Field<CommandRecordingSession>(window, "_session");
        var record = Button(window, "开始录制");
        Assert.Null(Active());
        Assert.Equal(60000, Field<System.Windows.Forms.Timer>(window, "_timeout").Interval);
        Click(record);
        Assert.Same(session, Active());
        var first = Command(1);
        session.Append(first);
        session.Append(Command(2));
        Assert.True(session.Paused);
        Assert.Single(session.Commands);
        Application.DoEvents();
        Assert.NotNull(window.Captured);
        Assert.Equal(first.Value, window.Captured.Value);
        Assert.NotSame(first, window.Captured);
        Assert.False(window.Captured.RequireAdvanceKey);
        Assert.Null(Active());
        Assert.False(Field<System.Windows.Forms.Timer>(window, "_timeout").Enabled);
    });

    [Fact]
    public void StartedCaptureCanSelectAnExternalPluginCommandFromTheList() => Sta(() =>
    {
        using var window = new CommandCaptureWindow();
        Prepare(window);
        var choices = Field<List<KeyboardShortcutRow>>(window, "_availableChoices");
        choices.Add(new KeyboardShortcutRow
        {
            Category = "第三方插件 > 博士钣金",
            Command = "STEP格式互换",
            ActionKind = ActionKinds.ExternalPlugin,
            Value = ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "STEP格式互换")
        });
        var picker = Field<ComboBox>(window, "_choices");
        picker.Items.Add("第三方插件 > 博士钣金 / STEP格式互换");
        Click(Button(window, "开始录制"));
        picker.SelectedIndex = 0;
        Application.DoEvents();

        Assert.NotNull(window.Captured);
        Assert.Equal(ActionKinds.ExternalPlugin, window.Captured.Kind);
        Assert.Equal(ExternalNamedCommandReference.Encode(1, "第三方插件 > 博士钣金", "STEP格式互换"), window.Captured.Value);
        Assert.False(Field<System.Windows.Forms.Timer>(window, "_timeout").Enabled);
        Assert.Null(Active());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BindingValidatesNameAndPreservesNativeOrExternalIdentity(bool external) => Sta(() =>
    {
        using var window = new CommandCaptureWindow();
        Prepare(window);
        var source = Command(1);
		if (external) source.Value = ExternalToolbarCommandReference.Encode(2, 41771, "真实插件命令");
        Click(Button(window, "开始录制"));
        Field<CommandRecordingSession>(window, "_session").Append(source);
        Application.DoEvents();
        var name = Field<TextBox>(window, "_name");
        var accept = Field<Button>(window, "_accept");
        Assert.True(name.Enabled); Assert.True(accept.Enabled);
        name.Text = "   ";
        Click(accept);
        Assert.Equal(DialogResult.None, window.DialogResult);
        Assert.False(window.IsDisposed);
        Assert.Contains("请填写功能名称", Field<Label>(window, "_status").Text);
        name.Text = "  我的快捷功能  ";
        Click(accept);
        Assert.Equal(DialogResult.OK, window.DialogResult);
        Assert.Equal("我的快捷功能", window.Captured.Label);
        Assert.Equal(source.Value, window.Captured.Value);
        Assert.Equal(source.Kind, window.Captured.Kind);
        Assert.Equal("原始功能", source.Label);
        Assert.Null(Active());
    });

    [Fact]
    public void RetryClearsTheOldIdentityAndTimeoutReleasesTheSession() => Sta(() =>
    {
        using var window = new CommandCaptureWindow();
        Prepare(window);
        var session = Field<CommandRecordingSession>(window, "_session");
        Click(Button(window, "开始录制"));
        session.Append(Command(1)); Application.DoEvents();
        Click(Button(window, "重新获取"));
        Assert.Null(window.Captured);
        Assert.Empty(session.Commands);
        Assert.False(Field<Button>(window, "_accept").Enabled);
        Assert.False(Field<TextBox>(window, "_name").Enabled);
        Assert.True(session.Paused);
        Assert.Null(Active());
        var timer = Field<System.Windows.Forms.Timer>(window, "_timeout");
        typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timer, new object[] { EventArgs.Empty });
        Assert.Null(Active()); Assert.False(timer.Enabled);
        Assert.Contains("未取得可识别", Field<Label>(window, "_status").Text);
        using var replacement = new CommandRecordingSession();
        replacement.Start();
        Assert.Same(replacement, Active());
    });

    [Fact]
    public void CancelAndDisposeReleaseWaitingCapture() => Sta(() =>
    {
        using (var window = new CommandCaptureWindow())
        {
            Prepare(window);
            Click(Button(window, "开始录制"));
            Click(Button(window, "取消"));
            Assert.Equal(DialogResult.Cancel, window.DialogResult);
            Assert.Null(Active());
        }
        var second = new CommandCaptureWindow();
        Prepare(second);
        Click(Button(second, "开始录制"));
        Assert.NotNull(Active());
        second.Dispose();
        Assert.Null(Active());
    });

    [Theory]
    [InlineData(560, 360)]
    [InlineData(500, 360)]
    public void CaptureLayoutKeepsIdentityNameAndActionsSeparate(int width, int height) => Sta(() =>
    {
        using var window = new CommandCaptureWindow { ClientSize = new Size(width, height) };
        Prepare(window);
        Click(Button(window, "开始录制"));
        Field<CommandRecordingSession>(window, "_session").Append(Command(1));
        Application.DoEvents(); window.PerformLayout();
        var status = Field<Label>(window, "_status");
        var name = Field<TextBox>(window, "_name");
        Rectangle nameBounds = window.RectangleToClient(name.RectangleToScreen(name.ClientRectangle));
        Assert.True(status.Bottom <= nameBounds.Top);
        foreach (Button button in Descendants(window).OfType<Button>())
        {
            Rectangle bounds = window.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            if (button.Text == "取消" || button.Text == "绑定到当前轮盘格" || button.Text == "重新获取")
                Assert.True(bounds.Top >= nameBounds.Bottom);
            Assert.True(window.ClientRectangle.Contains(bounds), button.Text + " is clipped");
        }
        string? capture = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            Directory.CreateDirectory(capture);
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(capture, "command-capture-" + width + ".png"));
        }
    });

    [Theory]
    [InlineData(720, 460)]
    [InlineData(560, 360)]
    public void RecorderLayoutKeepsStartAndFinishActionsVisible(int width, int height) => Sta(() =>
    {
        using var window = new CommandRecorderWindow { ClientSize = new Size(width, height) };
        Prepare(window);
        Assert.Contains("未开始", Field<Label>(window, "_status").Text);
        foreach (Button button in Descendants(window).OfType<Button>())
        {
            Rectangle bounds = window.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            Assert.True(window.ClientRectangle.Contains(bounds), button.Text + " is clipped");
        }
        var list = Field<ListView>(window, "_list");
        Assert.True(list.Height > 80);
        Assert.True(list.Bottom <= Field<Label>(window, "_status").Top);
        var instructions = window.Controls.OfType<Label>().Single(c => c.Dock == DockStyle.Top);
        int requiredTextHeight = TextRenderer.MeasureText(instructions.Text, instructions.Font,
            new Size(instructions.Width - instructions.Padding.Horizontal, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        Assert.True(instructions.Height - instructions.Padding.Vertical >= requiredTextHeight, "Recorder instructions are clipped");
        string? capture = Environment.GetEnvironmentVariable("XY_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            Directory.CreateDirectory(capture);
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(capture, "command-recorder-" + width + ".png"));
        }
    });

    private static ComboCommandConfig Command(int id) => new ComboCommandConfig
    { Label = "原始功能", Kind = ActionKinds.NativeCommand, Value = NativeCommandReference.Encode(id), RequireAdvanceKey = true };
    private static CommandRecordingSession? Active() => (CommandRecordingSession?)typeof(CommandRecordingSession)
        .GetField("_active", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static Button Button(Control parent, string caption) => Descendants(parent).OfType<Button>().Single(b => b.Text == caption);
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
    private static void Prepare(Form window)
    {
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.StartPosition = FormStartPosition.Manual;
        window.Location = new Point(-20000, -20000);
        window.Show();
        Application.DoEvents();
        window.PerformLayout();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

