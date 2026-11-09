using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FakeManager = XY.SolidWorksPlugin.Tests.WheelCommandTabMigrationTests.FakeManager;
using FakeBox = XY.SolidWorksPlugin.Tests.WheelCommandTabMigrationTests.FakeBox;

namespace XY.SolidWorksPlugin.Tests;

public sealed class WheelCommandTabConfigurationTests
{
    private static readonly int[] Commands = { 101, 102, 103, 104, 105, 106, 107 };

    [Fact]
    public void InactiveSchemaUpgradeAddsTheNewCommandWhenThatDocumentBecomesActive()
    {
        var manager = new FakeManager { FailCurrentRemoveOnceType = 2 };
        foreach (int type in WheelCommandTabs.DocumentTypes)
            manager.Seed(type, WheelCommandTabs.Title, true, Commands.Take(6).Concat(new[] { 991 }).ToArray());
        var deferred = new HashSet<int>();
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>(),
            WheelCommandTabs.Title, deferred, true);
        Assert.Contains(2, deferred);
        Assert.DoesNotContain(107, manager.Tabs[(2, WheelCommandTabs.Title)].Boxes.SelectMany(box => box.Commands));
        Assert.True(WheelCommandTabs.TryMaintainConfiguredCurrentDocumentTab(manager.Proxy, 2, Commands, WheelCommandTabs.Title,
            true, Array.Empty<int>(), WheelCommandTabs.Title, null, true));
        var actual = manager.Tabs[(2, WheelCommandTabs.Title)].Boxes.SelectMany(box => box.Commands).ToArray();
        Assert.All(Commands, id => Assert.Equal(1, actual.Count(value => value == id)));
        Assert.Contains(991, actual);
    }

    [Fact]
    public void DefaultConfiguredRegistrationShowsSevenStableCommandsInThreeBoxes()
    {
        var manager = new FakeManager();
        Assert.Equal(3, WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>()));
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            var tab = manager.Tabs[(type, WheelCommandTabs.Title)];
            Assert.True(tab.Visible); Assert.Equal(3, tab.Boxes.Count);
            Assert.Equal(new[] { 101, 105, 104 }, tab.Boxes[0].Commands);
            Assert.Equal(new[] { 102, 103, 106 }, tab.Boxes[1].Commands);
            Assert.Equal(new[] { 107 }, tab.Boxes[2].Commands);
        }
    }

    [Fact]
    public void HideAndShowPreserveAllUserButtonsStylesAndGrouping()
    {
        var manager = SeedCustomized();
        var originals = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value);
        var layouts = manager.Tabs.ToDictionary(pair => pair.Key, pair => Layout(pair.Value.Boxes));
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, false, Array.Empty<int>());
        Assert.All(manager.Tabs.Values, tab => Assert.False(tab.Visible));
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>());
        foreach (var pair in originals)
        {
            Assert.Same(pair.Value, manager.Tabs[pair.Key]);
            Assert.Equal(layouts[pair.Key], Layout(manager.Tabs[pair.Key].Boxes));
            Assert.True(manager.Tabs[pair.Key].Visible);
        }
        Assert.Empty(manager.Removed); Assert.Empty(manager.Added);
    }

    [Fact]
    public void RenameKeepsOriginalUserLayoutAndAddsImportedMacrosAsAnIndependentBox()
    {
        var manager = SeedCustomized();
        var before = manager.Tabs.ToDictionary(pair => pair.Key.Type, pair => pair.Value.Boxes.Select(box => (box.Commands.ToArray(), box.Styles.ToArray())).ToArray());
        const string title = "我的宏工具";
        WheelCommandTabs.ValidateConfiguredTitle(manager.Proxy, title, WheelCommandTabs.Title);
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, title, true, new[] { 201, 202 }, WheelCommandTabs.Title);
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            Assert.False(manager.Tabs.ContainsKey((type, WheelCommandTabs.Title)));
            var tab = manager.Tabs[(type, title)];
            Assert.True(tab.Visible); Assert.Equal(before[type].Length + 1, tab.Boxes.Count);
            for (int index = 0; index < before[type].Length; index++)
            {
                Assert.Equal(before[type][index].Item1, tab.Boxes[index].Commands);
                Assert.Equal(before[type][index].Item2, tab.Boxes[index].Styles);
            }
            Assert.Equal(new[] { 201, 202 }, tab.Boxes.Last().Commands);
        }
    }

    [Fact]
    public void NewTitleOccupiedByAnotherTabIsRejectedBeforeAnyMutation()
    {
        var manager = SeedCustomized();
        manager.Seed(2, "其他工具", false, new[] { 999 });
        var fingerprints = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value.Fingerprint());
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.ValidateConfiguredTitle(manager.Proxy, "其他工具", WheelCommandTabs.Title));
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, "其他工具", true, new[] { 201 }, WheelCommandTabs.Title));
        Assert.Equal(fingerprints.Count, manager.Tabs.Count);
        foreach (var pair in fingerprints) Assert.Equal(pair.Value, manager.Tabs[pair.Key].Fingerprint());
        Assert.Empty(manager.Removed); Assert.Empty(manager.Added);
    }

    [Fact]
    public void ReplacingMacrosRemovesOnlyKnownPreviousMacroIdsAndPreservesForeignCommands()
    {
        var manager = SeedCustomized();
        foreach (var tab in manager.Tabs.Values) tab.Boxes.Add(new FakeBox(new[] { 201, 997, 202 }, new[] { 61, 73, 62 }));
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, new[] { 202, 203 }, WheelCommandTabs.Title, null, false, new[] { 201, 202 });
        foreach (var tab in manager.Tabs.Values)
        {
            var commands = tab.Boxes.SelectMany(box => box.Commands).ToArray();
            Assert.DoesNotContain(201, commands); Assert.Equal(1, commands.Count(id => id == 202)); Assert.Equal(1, commands.Count(id => id == 203));
            Assert.All(Commands, id => Assert.Equal(1, commands.Count(existing => existing == id)));
            Assert.Equal(new[] { 997, 202 }, tab.Boxes.Where(box => box.Commands.Contains(997)).Single().Commands);
            Assert.Equal(new[] { 73, 62 }, tab.Boxes.Where(box => box.Commands.Contains(997)).Single().Styles);
            Assert.Contains(991, commands); Assert.Contains(992, commands);
        }
    }

    [Fact]
    public void ReapplyingTheSameMacroConfigurationKeepsUserPlacementAndStyles()
    {
        var manager = SeedCustomized();
        foreach (var tab in manager.Tabs.Values) tab.Boxes.Insert(0, new FakeBox(new[] { 201, 997, 202 }, new[] { 61, 73, 62 }));
        var layouts = manager.Tabs.ToDictionary(pair => pair.Key, pair => Layout(pair.Value.Boxes));
        var instances = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value);
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, new[] { 201, 202 }, WheelCommandTabs.Title, null, false, new[] { 201, 202 });
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, new[] { 201, 202 }, WheelCommandTabs.Title, null, false, new[] { 201, 202 });
        foreach (var pair in layouts)
        {
            Assert.Same(instances[pair.Key], manager.Tabs[pair.Key]);
            Assert.Equal(pair.Value, Layout(manager.Tabs[pair.Key].Boxes));
        }
        Assert.Empty(manager.Added); Assert.Empty(manager.Removed);
    }

    [Fact]
    public void SixCommandMigrationAppendsTheNewCommandWithoutLosingCustomLayout()
    {
        var manager = new FakeManager();
        var originalIds = new[] { 104, 991, 101, 105, 102, 103, 106 };
        var originalStyles = new[] { 7, 81, 11, 12, 13, 14, 15 };
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            var tab = manager.Seed(type, WheelCommandTabs.Title, true, originalIds);
            tab.Boxes[0].Styles = originalStyles.ToArray();
            tab.Boxes.Add(new FakeBox(new[] { 992 }, new[] { 89 }));
        }
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>(), WheelCommandTabs.Title, null, true);
        foreach (var tab in manager.Tabs.Values)
        {
            Assert.Equal(3, tab.Boxes.Count);
            Assert.Equal(originalIds, tab.Boxes[0].Commands); Assert.Equal(originalStyles, tab.Boxes[0].Styles);
            Assert.Equal(new[] { 992 }, tab.Boxes[1].Commands); Assert.Equal(new[] { 89 }, tab.Boxes[1].Styles);
            Assert.Equal(new[] { 107 }, tab.Boxes[2].Commands);
            Assert.All(Commands, id => Assert.Equal(1, tab.Boxes.SelectMany(box => box.Commands).Count(command => command == id)));
        }
    }

    [Fact]
    public void SchemaMigrationRemovesRepeatedBaseButtonsButKeepsTheirFirstStyleAndForeignButtons()
    {
        var manager = SeedCustomized();
        foreach (var tab in manager.Tabs.Values) tab.Boxes.Add(new FakeBox(new[] { 101, 997, 101, 105 }, new[] { 70, 71, 72, 73 }));
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>(), WheelCommandTabs.Title, null, true);
        foreach (var tab in manager.Tabs.Values)
        {
            var commands = tab.Boxes.SelectMany(box => box.Commands).ToArray();
            Assert.All(Commands, id => Assert.Equal(1, commands.Count(command => command == id)));
            Assert.Equal(Commands.Concat(new[] { 991 }), tab.Boxes[0].Commands);
            Assert.Equal(Enumerable.Range(20, 8), tab.Boxes[0].Styles);
            Assert.Equal(new[] { 992 }, tab.Boxes[1].Commands); Assert.Equal(new[] { 89 }, tab.Boxes[1].Styles);
            Assert.Equal(new[] { 997 }, tab.Boxes[2].Commands); Assert.Equal(new[] { 71 }, tab.Boxes[2].Styles);
        }
    }

    [Fact]
    public void RememberedTitleCannotClaimATabWhoseActualOwnedCommandsHaveBeenReplaced()
    {
        var manager = new FakeManager(); string title = "旧宏工具-" + Guid.NewGuid().ToString("N");
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, title, true, Array.Empty<int>());
        // This title is now in production's remembered set. Simulate a later
        // session where another tool occupies the same title in one document.
        manager.Tabs.Remove((2, title)); manager.Seed(2, title, false, new[] { 999, 998 });
        manager.Added.Clear(); manager.Removed.Clear();
        var fingerprints = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value.Fingerprint());
        var instances = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.ValidateConfiguredTitle(manager.Proxy, title, title, Commands));
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, title, true, Array.Empty<int>()));
        Assert.Throws<InvalidOperationException>(() => WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, WheelCommandTabs.Title, true, Array.Empty<int>(), title));
        Assert.Equal(fingerprints.Count, manager.Tabs.Count);
        foreach (var pair in fingerprints)
        {
            Assert.Same(instances[pair.Key], manager.Tabs[pair.Key]);
            Assert.Equal(pair.Value, manager.Tabs[pair.Key].Fingerprint());
        }
        Assert.Empty(manager.Added); Assert.Empty(manager.Removed);
    }

    [Fact]
    public void PartialRenameFailureRestoresEveryOriginalOwnedTabWithoutTouchingOtherTabs()
    {
        var manager = SeedCustomized(); const string title = "我的宏工具";
        foreach (int type in WheelCommandTabs.DocumentTypes) manager.Seed(type, "其他插件", type == 2, new[] { 900 + type });
        var fingerprints = manager.Tabs.ToDictionary(pair => pair.Key, pair => pair.Value.Fingerprint());
        var foreign = manager.Tabs.Where(pair => pair.Key.Title == "其他插件").ToDictionary(pair => pair.Key, pair => pair.Value);
        manager.FailConfiguredTitle = title; manager.FailConfiguredNewType = 3;
        Assert.ThrowsAny<Exception>(() => WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, title, false, new[] { 201 }, WheelCommandTabs.Title));
        Assert.Equal(fingerprints.Count, manager.Tabs.Count); Assert.DoesNotContain(manager.Tabs.Keys, key => key.Title == title);
        foreach (var pair in fingerprints) Assert.Equal(pair.Value, manager.Tabs[pair.Key].Fingerprint());
        foreach (var pair in foreign) Assert.Same(pair.Value, manager.Tabs[pair.Key]);
    }

    [Fact]
    public void DeferredInactiveTabRepairDoesNotAppendCommandsOnRepeatedMaintenance()
    {
        var manager = SeedCustomized(); const string title = "我的宏工具";
        manager.FailConfiguredTitle = WheelCommandTabs.Title; manager.FailConfiguredRemoveOnceType = 2;
        var deferred = new HashSet<int>();
        WheelCommandTabs.RegisterConfigured(manager.Proxy, Commands, title, true, new[] { 201 }, WheelCommandTabs.Title, deferred);
        // A host may support direct renaming without remove; either completion
        // route must keep exactly one copy of every imported/base button.
        if (deferred.Contains(2)) Assert.True(WheelCommandTabs.TryMaintainConfiguredCurrentDocumentTab(manager.Proxy, 2, Commands, title, true, new[] { 201 }, WheelCommandTabs.Title));
        var before = manager.Tabs[(2, title)].Fingerprint(); int additions = manager.Added.Count;
        Assert.True(WheelCommandTabs.TryMaintainConfiguredCurrentDocumentTab(manager.Proxy, 2, Commands, title, true, new[] { 201 }, WheelCommandTabs.Title));
        Assert.True(WheelCommandTabs.TryMaintainConfiguredCurrentDocumentTab(manager.Proxy, 2, Commands, title, true, new[] { 201 }, WheelCommandTabs.Title));
        Assert.Equal(before, manager.Tabs[(2, title)].Fingerprint()); Assert.Equal(additions, manager.Added.Count);
        Assert.False(manager.Tabs.ContainsKey((2, WheelCommandTabs.Title)));
        Assert.Equal(1, manager.Tabs[(2, title)].Boxes.SelectMany(box => box.Commands).Count(id => id == 201));
    }

    private static FakeManager SeedCustomized()
    {
        var manager = new FakeManager();
        foreach (int type in WheelCommandTabs.DocumentTypes)
        {
            manager.Seed(type, WheelCommandTabs.Title, type != 2, Commands.Concat(new[] { 991 }).ToArray());
            manager.Tabs[(type, WheelCommandTabs.Title)].Boxes.Add(new FakeBox(new[] { 992 }, new[] { 89 }));
        }
        return manager;
    }

    private static string Layout(IEnumerable<FakeBox> boxes) => string.Join("|", boxes.Select(box => string.Join(",", box.Commands) + "/" + string.Join(",", box.Styles)));
}
