using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelCommandTabMigrationTests
{
    private static readonly int[] Commands = { 101, 102, 103, 104, 105, 106, 107 };

    [Fact]
    public void CompletedRegistrationRetiresOnlyExactOldProductTitleAndPreservesUserTabs()
    {
        var manager = new FakeManager();
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            manager.Seed(type, WheelCommandTabs.Title, type != 2, new[] { 800 + type, 900 + type });
            manager.Seed(type, WheelCommandTabs.LegacyOwnedTitle, type != 3, new[] { 700 + type });
            manager.Seed(type, "小阳轮盘-其他插件", true, new[] { 600 + type });
            manager.Seed(type, "其他插件", false, new[] { 500 + type });
        }
        var retained = manager.Tabs.Where(pair => pair.Key.Title != WheelCommandTabs.LegacyOwnedTitle)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(3, WheelCommandTabs.Register(manager.Proxy, Commands, migrateLegacyGroup: false));
        Assert.DoesNotContain(manager.Tabs.Keys, key => key.Title == WheelCommandTabs.LegacyOwnedTitle);
        foreach (var original in retained) Assert.Same(original.Value, manager.Tabs[original.Key]);
        Assert.Equal(3, manager.Removed.Count);
        Assert.All(manager.Removed, key => Assert.Equal(WheelCommandTabs.LegacyOwnedTitle, key.Title));
        Assert.Empty(manager.Added);
        Assert.False(manager.Tabs[(2, WheelCommandTabs.Title)].Visible);
        Assert.Equal(new[] { 802, 902 }, manager.Tabs[(2, WheelCommandTabs.Title)].Boxes[0].Commands);
    }

    [Fact]
    public void FailedNewRegistrationLeavesLegacyTabsUntouchedAndRollsBackPartialNewTabs()
    {
        var manager = new FakeManager { FailNewType = 3 };
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            manager.Seed(type, WheelCommandTabs.LegacyOwnedTitle, type == 1, new[] { 700 + type });
            manager.Seed(type, "其他插件", true, new[] { 500 + type });
        }
        var previous = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.Register(manager.Proxy, Commands, false));
        Assert.Contains("add:3:" + WheelCommandTabs.Title, manager.Log);
        Assert.Equal(previous.Count, manager.Tabs.Count);
        foreach (var original in previous) Assert.Same(original.Value, manager.Tabs[original.Key]);
        Assert.DoesNotContain(manager.Removed, key => key.Title == WheelCommandTabs.LegacyOwnedTitle);
        Assert.DoesNotContain(manager.Tabs.Keys, key => key.Title == WheelCommandTabs.Title);
    }

    [Fact]
    public void InactiveLegacyCleanupDefersAndMatchingRuntimeTabsSurviveMigrationAndRetry()
    {
        var manager = new FakeManager { FailLegacyRemoveOnceType = 2 };
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            manager.Seed(type, WheelCommandTabs.Title, type != 2, Commands);
            manager.Seed(type, WheelCommandTabs.LegacyOwnedTitle, type != 1, new[] { 700 + type });
            manager.Seed(type, "其他插件", false, new[] { 500 + type });
        }
        var retained = manager.Tabs.Where(pair => pair.Key.Title != WheelCommandTabs.LegacyOwnedTitle).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(3, WheelCommandTabs.Register(manager.Proxy, Commands, true));
        Assert.True(manager.Tabs.ContainsKey((2, WheelCommandTabs.LegacyOwnedTitle)));
        Assert.False(manager.Tabs.ContainsKey((1, WheelCommandTabs.LegacyOwnedTitle)));
        Assert.False(manager.Tabs.ContainsKey((3, WheelCommandTabs.LegacyOwnedTitle)));
        Assert.True(WheelCommandTabs.TryRemoveLegacyOwnedTab(manager.Proxy, 2));
        Assert.DoesNotContain(manager.Tabs.Keys, key => key.Title == WheelCommandTabs.LegacyOwnedTitle);
        foreach (var original in retained) Assert.Same(original.Value, manager.Tabs[original.Key]);
        Assert.DoesNotContain(manager.Removed, key => key.Title != WheelCommandTabs.LegacyOwnedTitle);
        Assert.Empty(manager.Added);
        Assert.False(WheelCommandTabs.TryRemoveLegacyOwnedTab(manager.Proxy, 0));
        Assert.False(manager.Tabs[(2, WheelCommandTabs.Title)].Visible);
    }

    [Fact]
    public void FailedRebuildRestoresOriginalOwnedCommandsStylesAndVisibilityBeforeAliasCleanup()
    {
        var manager = new FakeManager { FailNewType = 3 };
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            manager.Seed(type, WheelCommandTabs.Title, type != 2, new[] { 800 + type, 900 + type });
            var legacy = manager.Seed(type, WheelCommandTabs.LegacyOwnedTitle, type != 1, new[] { 700 + type });
            legacy.Boxes.Add(new FakeBox(new[] { 1000 + type }, new[] { 300 + type }));
            manager.Seed(type, "其他插件", false, new[] { 500 + type });
        }
        var fingerprints = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value.Fingerprint());
        var untouched = manager.Tabs.Where(pair => pair.Key.Title != WheelCommandTabs.Title).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.Register(manager.Proxy, Commands, true));
        Assert.Equal(fingerprints.Count, manager.Tabs.Count);
        foreach (var original in fingerprints) Assert.Equal(original.Value, manager.Tabs[original.Key].Fingerprint());
        foreach (var original in untouched) Assert.Same(original.Value, manager.Tabs[original.Key]);
        Assert.DoesNotContain(manager.Removed, key => key.Title != WheelCommandTabs.Title);
        Assert.Contains("add:3:" + WheelCommandTabs.Title, manager.Log);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveDuplicateTabDefersWithoutAppendingAndRepairsOnlyWhenItsContextIsMaintained(bool reportRemovalSuccess)
    {
        var manager = new FakeManager { FailCurrentRemoveOnceType = 1, ReportRemovalSuccessWithoutDeleting = reportRemovalSuccess };
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            var tab = manager.Seed(type, WheelCommandTabs.Title, type != 1, Commands);
            tab.Boxes.Add(new FakeBox(new[] { 101, 105, 104 }, new[] { 1, 1, 1 }));
            tab.Boxes.Add(new FakeBox(new[] { 102, 103 }, new[] { 1, 1 }));
            manager.Seed(type, WheelCommandTabs.LegacyOwnedTitle, true, new[] { 700 + type });
            manager.Seed(type, "其他插件", false, new[] { 500 + type });
        }
        var originalPart = manager.Tabs[(1, WheelCommandTabs.Title)];
        var untouched = manager.Tabs.Where(pair => pair.Key.Title == "其他插件").ToDictionary(pair => pair.Key, pair => pair.Value);
        var deferred = new HashSet<int>();
        Assert.Equal(3, WheelCommandTabs.Register(manager.Proxy, Commands, true, deferred));
        Assert.Equal(new[] { 1 }, deferred);
        Assert.Same(originalPart, manager.Tabs[(1, WheelCommandTabs.Title)]);
        Assert.Equal(12, originalPart.Boxes.Sum(box => box.Commands.Length));
        Assert.True(manager.Tabs.ContainsKey((1, WheelCommandTabs.LegacyOwnedTitle)));
        int additionsBefore = manager.Added.Count;
        Assert.True(WheelCommandTabs.TryMaintainCurrentDocumentTab(manager.Proxy, 1, Commands));
        Assert.Equal(additionsBefore + 1, manager.Added.Count);
        var repaired = manager.Tabs[(1, WheelCommandTabs.Title)];
        Assert.Equal(3, repaired.Boxes.Count);
        Assert.Equal(Commands, repaired.Boxes.SelectMany(box => box.Commands).OrderBy(id => id));
        Assert.False(repaired.Visible);
        Assert.DoesNotContain(manager.Tabs.Keys, key => key.Title == WheelCommandTabs.LegacyOwnedTitle);
        foreach (var original in untouched) Assert.Same(original.Value, manager.Tabs[original.Key]);
    }

    internal sealed class FakeManager
    {
        internal readonly Dictionary<(int Type, string Title), FakeTab> Tabs = new Dictionary<(int, string), FakeTab>();
        internal readonly List<(int Type, string Title)> Removed = new List<(int, string)>();
        internal readonly List<(int Type, string Title)> Added = new List<(int, string)>();
        internal readonly List<string> Log = new List<string>();
        internal int FailNewType, FailLegacyRemoveOnceType, FailCurrentRemoveOnceType;
        internal int FailConfiguredNewType, FailConfiguredRemoveOnceType;
        internal string FailConfiguredTitle = "";
        internal bool ReportRemovalSuccessWithoutDeleting;
        internal readonly ICommandManager Proxy;

        internal FakeManager() => Proxy = Com<ICommandManager>(call =>
        {
            switch (call.MethodName)
            {
                case "GetCommandTab":
                    return Tabs.TryGetValue(((int)call.Args[0], (string)call.Args[1]), out var found) ? found.Proxy : null;
                case "AddCommandTab":
                    int type = (int)call.Args[0]; string title = (string)call.Args[1];
                    Log.Add("add:" + type + ":" + title);
                    if (type == FailNewType && title == WheelCommandTabs.Title) { FailNewType = 0; return null; }
                    if (type == FailConfiguredNewType && title == FailConfiguredTitle) { FailConfiguredNewType = 0; return null; }
                    Added.Add((type, title));
                    var tab = CreateTab(type, title);
                    Tabs.Add((type, title), tab);
                    return tab.Proxy;
                case "RemoveCommandTab":
                    var entry = Tabs.Single(pair => ReferenceEquals(pair.Value.Proxy, call.Args[0]));
                    Log.Add("remove:" + entry.Key.Type + ":" + entry.Key.Title);
                    if (entry.Key.Type == FailCurrentRemoveOnceType && entry.Key.Title == WheelCommandTabs.Title)
                    { FailCurrentRemoveOnceType = 0; return ReportRemovalSuccessWithoutDeleting; }
                    if (entry.Key.Type == FailLegacyRemoveOnceType && entry.Key.Title == WheelCommandTabs.LegacyOwnedTitle)
                    { FailLegacyRemoveOnceType = 0; return false; }
                    if (entry.Key.Type == FailConfiguredRemoveOnceType && entry.Key.Title == FailConfiguredTitle)
                    { FailConfiguredRemoveOnceType = 0; return false; }
                    Removed.Add(entry.Key); Tabs.Remove(entry.Key); return true;
                default: throw new InvalidOperationException("Unexpected command-manager operation: " + call.MethodName);
            }
        });

        internal FakeTab Seed(int type, string title, bool visible, int[] commands)
        {
            var tab = CreateTab(type, title); tab.Visible = visible;
            tab.Boxes.Add(new FakeBox(commands, commands.Select((_, index) => 20 + index).ToArray()));
            Tabs.Add((type, title), tab); return tab;
        }

        private FakeTab CreateTab(int type, string title)
        {
            FakeTab? tab = null;
            tab = new FakeTab(type, title, Log, (oldTitle, newTitle) =>
            {
                if (type == FailConfiguredNewType && newTitle == FailConfiguredTitle)
                { FailConfiguredNewType = 0; throw new InvalidOperationException("Configured rename rejected."); }
                if (Tabs.ContainsKey((type, newTitle))) throw new InvalidOperationException("Tab title already exists.");
                Tabs.Remove((type, oldTitle)); Tabs.Add((type, newTitle), tab!);
            });
            return tab;
        }
    }

    internal sealed class FakeTab
    {
        internal readonly List<FakeBox> Boxes = new List<FakeBox>();
        internal bool Visible;
        internal string CurrentTitle;
        internal readonly CommandTab Proxy;

        internal FakeTab(int type, string title, List<string> log, Action<string, string>? rename = null)
        {
          CurrentTitle = title;
          Proxy = Com<CommandTab>(call =>
          {
            switch (call.MethodName)
            {
                case "get_Name": return CurrentTitle;
                case "set_Name":
                    string next = (string)call.Args[0]; if (next != CurrentTitle) { rename?.Invoke(CurrentTitle, next); CurrentTitle = next; }
                    return null;
                case "get_Visible": return Visible;
                case "set_Visible": Visible = (bool)call.Args[0]; log.Add("visible:" + type + ":" + title); return null;
                case "GetCommandTabBoxCount": return Boxes.Count;
                case "CommandTabBoxes": return Boxes.Select(box => (object)box.Proxy).ToArray();
                case "AddCommandTabBox": var newBox = new FakeBox(new int[0], new int[0]); Boxes.Add(newBox); return newBox.Proxy;
                case "RemoveCommandTabBox": Boxes.Remove(Boxes.Single(box => ReferenceEquals(box.Proxy, call.Args[0]))); return null;
                default: throw new InvalidOperationException("Unexpected tab operation: " + call.MethodName);
            }
          });
        }

        internal string Fingerprint() => Visible + ";" + string.Join("|", Boxes.Select(box => string.Join(",", box.Commands) + "/" + string.Join(",", box.Styles)));
    }

    internal sealed class FakeBox
    {
        internal int[] Commands, Styles;
        internal readonly CommandTabBox Proxy;
        internal FakeBox(int[] commands, int[] styles)
        {
            Commands = commands.ToArray(); Styles = styles.ToArray();
            Proxy = Com<CommandTabBox>(call =>
            {
                switch (call.MethodName)
                {
                    case "GetCommands": return new BoxContents(Commands.ToArray(), Styles.ToArray());
                    case "AddCommands": Commands = Commands.Concat((int[])call.Args[0]).ToArray(); Styles = Styles.Concat((int[])call.Args[1]).ToArray(); return true;
                    case "RemoveCommands":
                        var remove = new HashSet<int>((int[])call.Args[0]);
                        var retained = Commands.Select((id, index) => (id, style: Styles[index])).Where(pair => !remove.Contains(pair.id)).ToArray();
                        Commands = retained.Select(pair => pair.id).ToArray(); Styles = retained.Select(pair => pair.style).ToArray(); return true;
                    default: throw new InvalidOperationException("Unexpected tab-box operation: " + call.MethodName);
                }
            });
        }
    }

    private sealed class BoxContents
    {
        internal readonly int[] Commands, Styles;
        internal BoxContents(int[] commands, int[] styles) { Commands = commands; Styles = styles; }
    }

    private static T Com<T>(Func<IMethodCallMessage, object?> action) => (T)new FakeCom(typeof(T), action).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _action;
        private readonly Type _type;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> action) : base(type) { _type = type; _action = action; }
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? result = call.MethodName == "GetType" ? _type : _action(call);
                // IMethodCallMessage.Args can return a fresh array. Marshal out
                // values through the exact argument array passed to ReturnMessage.
                var args = call.Args;
                if (result is BoxContents box) { args[0] = box.Commands; args[1] = box.Styles; result = box.Commands.Length; }
                return new ReturnMessage(result, args, args.Length, call.LogicalCallContext, call);
            }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
}
