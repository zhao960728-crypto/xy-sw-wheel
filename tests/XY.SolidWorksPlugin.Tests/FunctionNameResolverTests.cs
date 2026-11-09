using System.Collections.Generic;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class FunctionNameResolverTests
{
    [Theory]
    [InlineData("一键爆炸")]
    [InlineData("一键爆炸全部（自动散开）")]
    [InlineData("explode-all")]
    public void OneClickExplosionKeepsItsStableKeyForCurrentAndLegacyNames(string name)
    {
        Assert.True(BasicActions.TryResolve(name, out string key));
        Assert.Equal("explode-all", key);
        Assert.True(FunctionNameResolver.TryResolve(new List<KeyboardShortcutRow>(), name, out KeyboardShortcutRow row, out string message));
        Assert.Equal(ActionKinds.Basic, row.ActionKind);
        Assert.Equal("explode-all", row.Value);
        Assert.Empty(message);
        Assert.Equal("一键爆炸", BasicActions.Display(row.Value));
    }

    [Fact]
    public void ExplosionNameDoesNotOverrideAnotherCommandsSameCaption()
    {
        var rows = new[] { new KeyboardShortcutRow { Command = "一键爆炸", Category = "第三方插件 > Test", ActionKind = ActionKinds.ExternalPlugin,
            Value = ExternalNamedCommandReference.Encode(2, "第三方插件 > Test", "一键爆炸") } };
        Assert.False(FunctionNameResolver.TryResolve(rows, "一键爆炸", out _, out string message));
        Assert.Contains("多个命令", message);
    }

    [Fact]
    public void DirectChineseBasicNameResolvesToStableActionValue()
    {
        Assert.True(FunctionNameResolver.TryResolve(new List<KeyboardShortcutRow>(), "工序识别", out KeyboardShortcutRow row, out string message));
        Assert.Equal(ActionKinds.Basic, row.ActionKind);
        Assert.Equal("process-recognition", row.Value);
        Assert.Empty(message);
    }

    [Fact]
    public void DirectNativeNameKeepsItsEncodedCommandIdentity()
    {
        var rows = new[]
        {
            new KeyboardShortcutRow
            {
                Category = "SOLIDWORKS > 特征",
                Command = "边线法兰",
                ActionKind = ActionKinds.NativeCommand,
                Value = NativeCommandReference.Encode(359)
            }
        };

        Assert.True(FunctionNameResolver.TryResolve(rows, "边线法兰", out KeyboardShortcutRow row, out _));
        Assert.Equal(ActionKinds.NativeCommand, row.ActionKind);
        Assert.Equal(NativeCommandReference.Encode(359), row.Value);
    }

    [Fact]
    public void SameNamedCommandsAreRejectedInsteadOfGuessing()
    {
        var rows = new[]
        {
            new KeyboardShortcutRow { Category = "外部功能 > A", Command = "自动出图", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalToolbarCommandReference.Encode(1, 41001, "自动出图") },
            new KeyboardShortcutRow { Category = "外部功能 > B", Command = "自动出图", ActionKind = ActionKinds.ExternalPlugin, Value = ExternalToolbarCommandReference.Encode(1, 41002, "自动出图") }
        };

        Assert.False(FunctionNameResolver.TryResolve(rows, "自动出图", out _, out string message));
        Assert.Contains("多个命令", message);
    }

    [Fact]
    public void DuplicateCaptionsForOnePersistedCommandResolveOnce()
    {
        string value = ExternalToolbarCommandReference.Encode(1, 41001, "自动出图");
        var rows = new[]
        {
            new KeyboardShortcutRow { Category = "外部功能 > A", Command = "自动出图", ActionKind = ActionKinds.ExternalPlugin, Value = value },
            new KeyboardShortcutRow { Category = "第三方插件 > A", Command = "自动出图", ActionKind = ActionKinds.ExternalPlugin, Value = value }
        };

        Assert.True(FunctionNameResolver.TryResolve(rows, "自动出图", out KeyboardShortcutRow row, out string message));
        Assert.Equal(value, row.Value);
        Assert.Empty(message);
    }
}
