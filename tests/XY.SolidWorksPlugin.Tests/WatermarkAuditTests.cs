using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
public sealed class WatermarkAuditTests
{
    [Fact]
    public void MissingPropertyIsCreatedInTheDisplayedConfiguration()
    {
        var model = new ModelFixture();
        WatermarkPropertyAccess.Value target = WatermarkPropertyAccess.Read(model.Document, "引用", "自定义属性", "工序");
        Assert.False(target.Exists);
        Assert.Equal("引用", target.Configuration);
        WatermarkPropertyAccess.Write(model.Document, target, "下料-检验");
        Assert.Equal("下料-检验", model.Properties("引用")["工序"].Raw);
        Assert.Empty(model.Properties(""));
        Assert.Equal(1, model.Writes);
    }

    [Fact]
    public void EditingDocumentFallbackRejectsNewConfigurationOverride()
    {
        var model = new ModelFixture();
        model.Set("", "工序", "原文档工序");
        var target = WatermarkPropertyAccess.Read(model.Document, "引用", "自定义属性", "工序");
        model.Set("引用", "工序", "外部配置工序");
        var failure = Assert.Throws<InvalidOperationException>(() => WatermarkPropertyAccess.Write(model.Document, target, "旧草稿"));
        Assert.Contains("有效来源", failure.Message);
        Assert.Equal("原文档工序", model.Properties("")["工序"].Raw);
        Assert.Equal("外部配置工序", model.Properties("引用")["工序"].Raw);
        Assert.Equal(0, model.Writes);
    }

    [Fact]
    public void EditingRejectsAConcurrentPropertyTypeChange()
    {
        var model = new ModelFixture();
        model.Set("引用", "数量", "12");
        var target = WatermarkPropertyAccess.Read(model.Document, "引用", "自定义属性", "数量");
        model.Properties("引用")["数量"].Type = 3;
        Assert.Throws<InvalidOperationException>(() => WatermarkPropertyAccess.Write(model.Document, target, "13"));
        Assert.Equal("12", model.Properties("引用")["数量"].Raw);
        Assert.Equal(0, model.Writes);
    }

    [Fact]
    public void EditingRejectsAChangedAliasEvenWhenItsTextMatches()
    {
        var model = new ModelFixture();
        model.Set("引用", "工艺流程", "相同内容");
        var target = WatermarkPropertyAccess.Read(model.Document, "引用", "自定义属性", "工序");
        model.Set("引用", "工序", "相同内容");
        Assert.Throws<InvalidOperationException>(() => WatermarkPropertyAccess.Write(model.Document, target, "旧草稿"));
        Assert.Equal(0, model.Writes);
    }

    [Fact]
    public void SavingSettingsMergesUntouchedWatermarkFieldsAndOtherModules() => Sta(() =>
    {
        var live = new PluginConfig();
        var before = live;
        using var settings = new PropertyWatermarkSettingsWindow(() => live, value =>
        {
            Assert.Equal(9, before.PropertyWatermark.FontSize);
            live = value;
        }, () => null!, () => { });
        live.RadialMenuEnabled = false;
        live.PropertyWatermark.OpacityPercent = 61;
        live.PropertyWatermark.PositionMode = "custom";
        live.PropertyWatermark.OffsetX = 321;
        live.PropertyWatermark.Fields.Add(new PropertyWatermarkField { DisplayName = "外部字段", PropertyName = "外部字段" });
        Field<NumericUpDown>(settings, "_fontSize").Value = 12;
        Call(settings, "ApplyConfig");
        Assert.False(live.RadialMenuEnabled);
        Assert.Equal(12, live.PropertyWatermark.FontSize);
        Assert.Equal(61, live.PropertyWatermark.OpacityPercent);
        Assert.Equal(321, live.PropertyWatermark.OffsetX);
        Assert.Contains(live.PropertyWatermark.Fields, field => field.PropertyName == "外部字段");
    });

    [Fact]
    public void FailedSettingsSaveDoesNotMutateTheLiveConfiguration() => Sta(() =>
    {
        var live = new PluginConfig();
        using var settings = new PropertyWatermarkSettingsWindow(() => live, _ => throw new IOException("测试保存失败"), () => null!, () => { });
        string before = ConfigStore.Serialize(live);
        Field<NumericUpDown>(settings, "_displayScale").Value = 140;
        var error = Assert.Throws<TargetInvocationException>(() => Call(settings, "ApplyConfig"));
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(before, ConfigStore.Serialize(live));
    });

    [Fact]
    public void ConcurrentChangesOfTheSameSettingAreReportedWithoutSaving() => Sta(() =>
    {
        var live = new PluginConfig();
        int saves = 0;
        using var settings = new PropertyWatermarkSettingsWindow(() => live, _ => saves++, () => null!, () => { });
        Field<NumericUpDown>(settings, "_fontSize").Value = 12;
        live.PropertyWatermark.FontSize = 15;
        var error = Assert.Throws<TargetInvocationException>(() => Call(settings, "ApplyConfig"));
        Assert.Contains("字号", error.InnerException!.Message);
        Assert.Equal(15, live.PropertyWatermark.FontSize);
        Assert.Equal(12, Field<NumericUpDown>(settings, "_fontSize").Value);
        Assert.Equal(0, saves);
    });

    [Fact]
    public void SettingsPreviewClearsOldValuesAndOriginsAfterAHostReadFailure() => Sta(() =>
    {
        var model = new ModelFixture();
        model.Set("引用", "工序", "旧工序");
        bool broken = false;
        var host = Proxy<ISldWorks>(call => call.MethodName == "get_ActiveDoc" ? broken ? throw new InvalidOperationException("文档已关闭") : model.Document : null);
        var config = new PluginConfig { PropertyWatermark = WatermarkConfig() };
        using var settings = new PropertyWatermarkSettingsWindow(() => config, _ => { }, () => host, () => { });
		settings.ShowInTaskbar = false; settings.Opacity = 0; settings.Show(); Application.DoEvents();
        var grid = Field<DataGridView>(settings, "_grid");
        Assert.Equal("旧工序", grid.Rows[0].Cells["ResolvedValue"].Value);
        Assert.Contains("引用", grid.Rows[0].Cells["ResolvedValue"].ToolTipText);
        broken = true;
        Call(settings, "RefreshProperties");
        Assert.Equal("", grid.Rows[0].Cells["ResolvedValue"].Value);
        Assert.Equal("", grid.Rows[0].Cells["ResolvedValue"].ToolTipText);
        Assert.Contains("读取失败", Field<Label>(settings, "_readStatus").Text);
    });

    [Fact]
    public void HostResizeRefitsUnchangedTextAndPreservesLiteralAmpersands() => Sta(() =>
    {
        using var owner = HostForm(1000);
        var model = new ModelFixture();
        model.Set("引用", "工序", "A&B " + new string('工', 130));
        var config = WatermarkConfig();
        using var watermark = new PropertyWatermarkWindow(() => owner.Handle, () => model.Host, () => config, () => { });
        watermark.RefreshNow();
        string before = Field<Label>(watermark, "_content").Text;
        string raw = Field<string>(watermark, "_lastText");
        owner.Width = 380;
        watermark.RefreshNow();
        Assert.True(Field<Label>(watermark, "_content").Text.Length < before.Length);
        Assert.Equal(raw, Field<string>(watermark, "_lastText"));
        Assert.True(watermark.Width <= owner.Width);
        Assert.False(Field<Label>(watermark, "_content").UseMnemonic);
        Assert.False(Field<Label>(watermark, "_caption").UseMnemonic);
    });

    [Fact]
    public void SwitchingReferencedConfigurationOfTheSamePartCancelsTheOpenEditor() => Sta(() =>
    {
        using var owner = HostForm(700);
        var part = new ModelFixture();
        part.Set("引用", "工序", "旧配置工序");
        part.Set("新引用", "工序", "新配置工序");
        string reference = "引用";
        var component = Proxy<Component2>(call => call.MethodName switch { "GetModelDoc2" => part.Document, "get_ReferencedConfiguration" => reference, _ => null });
        var selection = Proxy<SelectionMgr>(call => call.MethodName switch { "GetSelectedObjectCount2" => 1, "GetSelectedObject6" or "GetSelectedObjectsComponent4" => component, _ => null });
        var assembly = new ModelFixture(2, selection);
        var config = WatermarkConfig(); config.ReadSelectedPartProperties = true;
        using var watermark = new PropertyWatermarkWindow(() => owner.Handle, () => assembly.Host, () => config, () => { });
        watermark.RefreshNow();
        Call(watermark, "EditProperty", watermark, new MouseEventArgs(MouseButtons.Left, 2, 1, 1, 0));
        Application.DoEvents();
        Assert.True(Field<bool>(watermark, "_editing"));
        var editor = Field<PluginToolWindow>(watermark, "_editDialog");
        Assert.True(editor.Visible);
        reference = "新引用";
        watermark.RefreshNow();
        Application.DoEvents();
        watermark.RefreshNow();
        Assert.True(editor.IsDisposed);
        Assert.False(Field<bool>(watermark, "_editing"));
        Assert.Contains("新配置工序", Field<Label>(watermark, "_content").Text);
        Assert.DoesNotContain("旧配置工序", Field<Label>(watermark, "_content").Text);
        Assert.Equal("新引用", Field<string>(watermark, "_displayedConfiguration"));
        Assert.Equal(0, part.Writes);
    });

    [Fact]
    public void CollapsedWatermarkKeepsItsStateAndExpandsWithTheLatestValue() => Sta(() =>
    {
        using var owner = HostForm(700);
        var model = new ModelFixture(); model.Set("引用", "工序", "原工序");
        var config = WatermarkConfig();
        using var watermark = new PropertyWatermarkWindow(() => owner.Handle, () => model.Host, () => config, () => { });
        watermark.RefreshNow();
        int expandedHeight = watermark.Height;
        Call(watermark, "ToggleExpanded");
        model.Set("引用", "工序", "外部更新工序");
        watermark.RefreshNow();
        Assert.False(Field<Label>(watermark, "_content").Visible);
        Assert.True(watermark.Height < expandedHeight);
        Call(watermark, "ToggleExpanded");
        Assert.True(Field<Label>(watermark, "_content").Visible);
        Assert.Contains("外部更新工序", Field<Label>(watermark, "_content").Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedDocumentOrDisableClearsTheDisplayedIdentity(bool disable) => Sta(() =>
    {
        using var owner = HostForm(700);
        var model = new ModelFixture(); model.Set("引用", "工序", "原工序");
        var config = WatermarkConfig();
        using var watermark = new PropertyWatermarkWindow(() => owner.Handle, () => model.Host, () => config, () => { });
        watermark.RefreshNow();
        if (disable) config.Enabled = false; else model.DocumentType = 3;
        watermark.RefreshNow();
        Assert.False(watermark.Visible);
        Assert.Null(Field<IModelDoc2>(watermark, "_displayedDocument"));
        Assert.Equal("", Field<string>(watermark, "_displayedConfiguration"));
        Assert.Equal("", Field<Label>(watermark, "_content").Text);
    });

    private static PropertyWatermarkConfig WatermarkConfig() => new PropertyWatermarkConfig
    {
        Enabled = true, Fields = new List<PropertyWatermarkField> { new PropertyWatermarkField { DisplayName = "工序", PropertyName = "工序" } }
    };

    private static Form HostForm(int width)
    {
        var form = new Form { Width = width, Height = 580, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(10, 10) };
        form.Show(); return form;
    }

    private sealed class Property
    {
        internal string Raw = "";
        internal string Resolved = "";
        internal int Type = 30;
    }

    private sealed class ModelFixture
    {
        private readonly Dictionary<string, Dictionary<string, Property>> _scopes = new Dictionary<string, Dictionary<string, Property>>();
        internal readonly IModelDoc2 Document;
        internal readonly ISldWorks Host;
        internal int Writes;
        internal int DocumentType;
        internal string ActiveConfiguration = "引用";
        internal Dictionary<string, Property> Properties(string scope)
        {
            if (!_scopes.TryGetValue(scope, out var values)) _scopes[scope] = values = new Dictionary<string, Property>(StringComparer.OrdinalIgnoreCase);
            return values;
        }
        internal void Set(string scope, string name, string text) => Properties(scope)[name] = new Property { Raw = text, Resolved = text };
        internal ModelFixture(int type = 1, ISelectionMgr? selection = null)
        {
            DocumentType = type;
            var configuration = Proxy<Configuration>(call => call.MethodName == "get_Name" ? ActiveConfiguration : null);
            var manager = Proxy<ConfigurationManager>(call => call.MethodName == "get_ActiveConfiguration" ? configuration : null);
            var extension = Proxy<ModelDocExtension>(call => call.MethodName == "get_CustomPropertyManager" ? Manager((string)call.Args[0]) : null);
            Document = Proxy<IModelDoc2>(call => call.MethodName switch
            {
                "GetType" => DocumentType, "GetTitle" => "模型 A&B", "GetPathName" => "", "get_ConfigurationManager" => manager,
                "get_Extension" => extension, "get_ISelectionManager" => selection, "IsOpenedReadOnly" => false, _ => null
            });
            Host = Proxy<ISldWorks>(call => call.MethodName == "get_ActiveDoc" ? Document : null);
        }
        private ICustomPropertyManager Manager(string scope) => Proxy<CustomPropertyManager>(call =>
        {
            var values = Properties(scope);
            string name = call.Args.Length > 0 ? call.Args[0] as string ?? "" : "";
            switch (call.MethodName)
            {
                case "GetNames": return values.Keys.Cast<object>().ToArray();
                case "Get4": return values.TryGetValue(name, out var value) ? value : new Property();
                case "GetType2": return values.TryGetValue(name, out var typed) ? typed.Type : 0;
                case "Set2": Writes++; Set(scope, name, (string)call.Args[1]); return 0;
                case "Add3": Writes++; Set(scope, name, (string)call.Args[2]); return 0;
                case "Delete2": Writes++; values.Remove(name); return 0;
                default: return null;
            }
        });
    }
    private static T Proxy<T>(Func<IMethodCallMessage, object?> invoke) => (T)new FakeCom(typeof(T), invoke).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _invoke;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> invoke) : base(type) { _invoke = invoke; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? value = _invoke(call);
                var args = call.Args;
                if (value is Property property) { args[2] = property.Raw; args[3] = property.Resolved; value = true; }
                return new ReturnMessage(value, args, args.Length, call.LogicalCallContext, call);
            }
            catch (Exception ex) { return new ReturnMessage(ex, call); }
        }
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
