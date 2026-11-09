using System;
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
public sealed class RecordingPathIdentityTests
{
    [Theory]
    [InlineData("工具 > 插件甲 > 子菜单", "工具 > 插件甲 > 子菜单")]
    [InlineData("第三方插件 > 工具 > 插件甲 > 子菜单", "工具 > 插件甲 > 子菜单")]
    public void RecordingKeepsTheCompleteLivePathAndDocumentContext(string livePath, string expectedPath)
    {
        Assert.True(CommandRecordingSession.TryCreateRecordedCommand(51001, false, 2, "共享功能", livePath, out var command, out var issue), issue);
        Assert.Equal(ActionKinds.ExternalPlugin, command.Kind);
        Assert.True(ExternalNamedCommandReference.TryDecode(command.Value, out int documentType, out string category, out string name));
        Assert.Equal(2, documentType); Assert.Equal("第三方插件 > " + expectedPath, category); Assert.Equal("共享功能", name);
        Assert.True(command.RequireAdvanceKey);
        Assert.True(CommandRecordingSession.TryCreateRecordedCommand(51001, false, 2, "共享功能", "工具 > 插件乙 > 子菜单", out var other, out _));
        Assert.NotEqual(command.Value, other.Value);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(1, "未分类插件")]
    [InlineData(0, "插件甲")]
    [InlineData(4, "插件甲")]
    public void MissingPathOrInvalidDocumentNeverDegradesToAnIdAndNameGuess(int documentType, string livePath)
    {
        Assert.False(CommandRecordingSession.TryCreateRecordedCommand(51001, false, documentType, "共享功能", livePath, out var command, out var issue));
        Assert.Null(command); Assert.NotEmpty(issue);
    }

    [Fact]
    public void NativeZeroRemainsValidWhileExternalZeroIsRejected()
    {
        Assert.True(CommandRecordingSession.TryCreateRecordedCommand(0, true, 0, "打开", "", out var command, out var issue), issue);
        Assert.Equal(ActionKinds.NativeCommand, command.Kind);
        Assert.Equal(NativeCommandReference.Encode(0), command.Value);
        Assert.False(CommandRecordingSession.TryCreateRecordedCommand(0, false, 1, "插件功能", "插件甲", out _, out _));
        Assert.False(CommandRecordingSession.TryCreateRecordedCommand(int.MaxValue, true, 1, "未知功能", "", out _, out _));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(161, "")]
    [InlineData(161, "   ")]
    public void KnownNativeWithoutAMenuCaptionKeepsItsExactId(int commandId, string? caption)
    {
        Assert.True(CommandRecordingSession.TryCreateRecordedCommand(commandId, true, 1, caption!, "", out var command, out var issue), issue);
        Assert.Equal(ActionKinds.NativeCommand, command.Kind);
        Assert.StartsWith("swCommands_", command.Label);
        Assert.True(NativeCommandReference.TryDecodeId(command.Value, out int recordedId));
        Assert.Equal(commandId, recordedId);
    }

    [Fact]
    public void EmptyCaptionStillRejectsUnidentifiedNativeOrExternalCommands()
    {
        Assert.False(CommandRecordingSession.TryCreateRecordedCommand(int.MaxValue, true, 1, "", "", out _, out _));
        Assert.False(CommandRecordingSession.TryCreateRecordedCommand(51001, false, 1, "", "工具 > 插件甲", out _, out _));
    }

    [Fact]
    public void SingleCaptureShowsWhyAPathWasRejectedAndKeepsWaiting() => Sta(() =>
    {
        using var window = new CommandCaptureWindow { ShowInTaskbar = false, Opacity = 0 };
        window.Show(); Application.DoEvents();
        var session = Field<CommandRecordingSession>(window, "_session");
        typeof(CommandCaptureWindow).GetMethod("StartCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        CommandRecordingSession.TryCreateRecordedCommand(51001, false, 1, "共享功能", "", out _, out string issue);
        typeof(CommandRecordingSession).GetField("<LastIssue>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, issue);
        typeof(CommandCaptureWindow).GetMethod("SessionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Application.DoEvents();
        Assert.Contains("完整路径", Field<Label>(window, "_status").Text);
        Assert.Contains("继续等待", Field<Label>(window, "_status").Text);
        Assert.Null(window.Captured); Assert.False(Field<Button>(window, "_accept").Enabled);
        Assert.True(Field<System.Windows.Forms.Timer>(window, "_timeout").Enabled);
    });

    [Theory]
    [InlineData("博士钣金", "DoctorSheetMetal_Plus AddIn", true)]
    [InlineData("Dr工具", "Doctor Tools", true)]
    [InlineData("Doctor Tools", "DoctorSheetMetal_Plus AddIn", false)]
    [InlineData("琴剑钣金", "琴剑小工具", false)]
    [InlineData("嘉立创", "嘉立创测试插件", false)]
    [InlineData("插件", "插件乙", false)]
    public void UiRoutesAcceptExplicitAliasesWithoutSubstitutingAnotherModule(string current, string recorded, bool expected)
        => Assert.Equal(expected, QinJianFunctionReader.PluginTabNamesMatchForTest(current, recorded));

    [Fact]
    public void DuplicateTabAliasesAreAmbiguousEvenWhenAnExactCaptionExists()
    {
        int index = QinJianFunctionReader.FindUniqueTabIndex(new[] { "博士钣金", "DoctorSheetMetal_Plus AddIn" }, "博士钣金", true, out bool ambiguous);
        Assert.Equal(-1, index); Assert.True(ambiguous);
        Assert.Equal(1, QinJianFunctionReader.FindUniqueTabIndex(new[] { "琴剑小工具", "QinJian琴剑钣金" }, "琴剑钣金", false, out ambiguous));
        Assert.False(ambiguous);
    }

    [Theory]
    [InlineData("external-plugin-ui:plugin/%ZZ")]
    [InlineData("external-plugin-ui:plugin/%")]
    [InlineData("external-plugin-ui:plugin/function/other")]
    [InlineData("external-plugin-ui:plugin/function%0Aother")]
    public void MalformedUiReferencesCannotBecomeExecutablePaths(string value)
        => Assert.False(ExternalPluginActionValue.TryParse(value, out _, out _));

    [Fact]
    public void CallbackResolutionRejectsConflictingTargetsButKeepsIdenticalDatabaseDuplicates()
    {
        var first = Definition("回调甲"); var duplicate = Definition("回调甲"); var wrong = Definition("回调乙");
        Assert.True(QinJianCommandCatalog.TryResolveDefinition(new[] { first, duplicate }, "琴剑钣金", "&共享功能...", out var resolved, out _));
        Assert.Same(first, resolved);
        Assert.False(QinJianCommandCatalog.TryResolveDefinition(new[] { first, wrong }, "琴剑钣金", "共享功能", out _, out var issue));
        Assert.Contains("多个不同回调", issue);
        Assert.False(QinJianCommandCatalog.TryResolveDefinition(new[] { first }, "琴剑小工具", "共享功能", out _, out _));
    }

    [Fact]
    public void CallbackTypeNeverFallsBackToASimilarlyNamedMethodInAnotherClass()
    {
        Assembly assembly = typeof(RecordingPathIdentityTests).Assembly;
        Assert.Equal(typeof(CallbackHost), QinJianCommandCatalog.FindAddinType(assembly, typeof(CallbackHost).FullName!, "执行"));
        Assert.Null(QinJianCommandCatalog.FindAddinType(assembly, "Missing.SwAddin", "执行"));
        Assert.Null(QinJianCommandCatalog.FindAddinType(assembly, typeof(CallbackHost).FullName!, "读取"));
    }

    [Fact]
    public void CallbackReceivesTheSuppliedHostInstance()
    {
        var proxy = new HostProxy();
        var supplied = (ISldWorks)proxy.GetTransparentProxy();
        var callback = new CallbackHost();
        QinJianCommandCatalog.SetSolidWorksField(callback, typeof(CallbackHost), supplied);
        Assert.Same(supplied, callback.iSwApp);
        Assert.Equal(0, proxy.Calls);
    }

    public sealed class CallbackHost
    {
        public object? iSwApp;
        public void 执行() { }
        public int 读取() => 1;
    }

    private sealed class HostProxy : RealProxy
    {
        internal int Calls;
        internal HostProxy() : base(typeof(ISldWorks)) { }
        public override IMessage Invoke(IMessage message)
        {
            Calls++; var call = (IMethodCallMessage)message;
            return new ReturnMessage(null, call.Args, call.Args.Length, call.LogicalCallContext, call);
        }
    }
    private static QinJianCommandCatalog.QinJianCommandDefinition Definition(string callback) => new("琴剑钣金", "共享功能", callback, @"C:\fixture\QinJian.dll", "QinJian.SwAddin");
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
