using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class UserMacroStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "XY-macro-store-tests-" + Guid.NewGuid().ToString("N"));
    private string Storage => Path.Combine(_root, "owned");

    [Fact]
    public void ImportCopiesOnlyTheSelectedFileAndPreservesExactEntryWithoutExecuting()
    {
        string source = Source();
        var command = UserMacroStore.Import(source, "  我的宏  ", "ExactModule", "main", null, Storage);
        Assert.True(Guid.TryParseExact(command.Id, "N", out _));
        Assert.Equal("我的宏", command.Name); Assert.Equal("ExactModule", command.ModuleName); Assert.Equal("main", command.ProcedureName);
        Assert.Equal(Path.Combine(Storage, command.Id, "selected.swb"), command.FilePath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(command.FilePath)); Assert.True(File.Exists(source));
        Assert.True(command.CommandUserId >= UserMacroStore.FirstCommandUserId);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(command.FilePath)!));
    }

    [Fact]
    public void DeletedAndCancelledImportsNeverRecycleAnOldToolbarIdentity()
    {
        string source = Source();
        var first = UserMacroStore.Import(source, "first", "Module1", "main", null, Storage);
        UserMacroStore.DeleteOwnedImportedFile(first, Array.Empty<UserMacroCommand>(), Storage);
        var second = UserMacroStore.Import(source, "second", "Module1", "main", null, Storage);
        Assert.True(second.CommandUserId > first.CommandUserId);
        Assert.NotEqual(first.Id, second.Id); Assert.False(File.Exists(first.FilePath)); Assert.True(File.Exists(source));
    }

    [Fact]
    public void TheAllocatorAlsoRespectsImportedConfigurationIdsFromAnotherMachine()
    {
        var previous = new UserMacroCommand { CommandUserId = 23000 };
        var command = UserMacroStore.Import(Source(), "new macro", "Module1", "main", new[] { previous }, Storage);
        Assert.Equal(23001, command.CommandUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("轮盘设置")]
    [InlineData("加入宏")]
    [InlineData("same NAME")]
    public void InvalidOrDuplicateFunctionNamesDoNotCopyOrAllocateAnything(string name)
    {
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.Import(Source(), name, "Module1", "main",
            new[] { new UserMacroCommand { Name = "Same Name" } }, Storage));
        Assert.False(Directory.Exists(Storage));
    }

    [Fact]
    public void ArbitraryDllAndMissingEntryAreRejectedBeforeAnyFileIsCopied()
    {
        string dll = Source("selected.dll");
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.Import(dll, "dll", "Module", "main", null, Storage));
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.Import(Source("selected.swp"), "empty", "", "main", null, Storage));
        Assert.False(Directory.Exists(Storage));
    }

    [Fact]
    public void CancelledDraftCleanupCannotDeleteACommittedFileOrTheOriginalUserFile()
    {
        string source = Source();
        var saved = UserMacroStore.Import(source, "saved", "Module1", "main", null, Storage);
        var draft = UserMacroStore.Import(source, "draft", "Module1", "main", new[] { saved }, Storage);
        UserMacroStore.DeleteOwnedImportedFile(saved, new[] { saved }, Storage);
        UserMacroStore.DeleteOwnedImportedFile(draft, new[] { saved }, Storage);
        Assert.True(File.Exists(saved.FilePath)); Assert.False(File.Exists(draft.FilePath)); Assert.True(File.Exists(source));
    }

    [Fact]
    public void CleanupRejectsPathEscapeAndNeverRecursivelyDeletesExtraUserFiles()
    {
        string source = Source();
        var command = UserMacroStore.Import(source, "test", "Module1", "main", null, Storage);
        string imported = command.FilePath;
        command.FilePath = source;
        UserMacroStore.DeleteOwnedImportedFile(command, null, Storage);
        Assert.True(File.Exists(source)); Assert.True(File.Exists(imported));
        command.FilePath = imported;
        string extra = Path.Combine(Path.GetDirectoryName(imported)!, "keep.txt"); File.WriteAllText(extra, "keep");
        UserMacroStore.DeleteOwnedImportedFile(command, null, Storage);
        Assert.False(File.Exists(imported)); Assert.True(File.Exists(extra));
    }

    [Fact]
    public void ExactStoredPathModuleAndProcedureReachTheRunnerOnceOnlyOnExplicitExecution()
    {
        var command = UserMacroStore.Import(Source(), "test", "ExactModule", "ExactProcedure", null, Storage);
        int calls = 0;
        UserMacroStore.Execute(command, (string path, string module, string procedure, int options, out int error) =>
        {
            calls++; Assert.Equal(command.FilePath, path); Assert.Equal("ExactModule", module); Assert.Equal("ExactProcedure", procedure);
            error = 0; return true;
        }, Storage);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MissingFileOrForeignPathCannotReachTheRunner()
    {
        var command = UserMacroStore.Import(Source(), "test", "Module1", "main", null, Storage);
        File.Delete(command.FilePath);
        UserMacroStore.Runner runner = (string path, string module, string procedure, int options, out int error) =>
        { error = 0; Assert.Fail("An invalid path must never run."); return false; };
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.Execute(command, runner, Storage));
        command.FilePath = Source();
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.Execute(command, runner, Storage));
    }

    [Fact]
    public void HostFailureReportsExactIdentityWithoutRetry()
    {
        var command = UserMacroStore.Import(Source(), "my function", "Module1", "main", null, Storage);
        int calls = 0;
        var error = Assert.Throws<InvalidOperationException>(() => UserMacroStore.Execute(command,
            (string path, string module, string procedure, int options, out int code) => { calls++; code = 7; return false; }, Storage));
        Assert.Equal(1, calls); Assert.Contains("my function", error.Message); Assert.Contains("Module1.main", error.Message);
        Assert.Contains(command.FilePath, error.Message); Assert.Contains("7", error.Message);
    }

    [Fact]
    public void DuplicateStoredIdsAreRejectedRatherThanSelectingTheFirstMacro()
    {
        var command = UserMacroStore.Import(Source(), "first", "Module1", "main", null, Storage);
        var duplicate = new UserMacroCommand { Id = Guid.NewGuid().ToString("N"), CommandUserId = command.CommandUserId, Name = "second" };
        var config = new PluginConfig { UserMacros = new List<UserMacroCommand> { command, duplicate } };
        Assert.Throws<InvalidOperationException>(() => UserMacroStore.ValidateConfiguration(config, Storage));
    }

    [Fact]
    public void EntryEnumerationPreservesModuleAndProcedureAndDoesNotInventAMain()
    {
        var entries = UserMacroStore.ParseEntryPoints(new[] { "ModuleB.other", "ModuleA.main", "modulea.MAIN", "broken", "NoProcedure." });
        Assert.Equal(2, entries.Count); Assert.Equal("ModuleA.main", entries[0].ToString()); Assert.Equal("ModuleB.other", entries[1].ToString());
        Assert.Empty(UserMacroStore.ParseEntryPoints(null));
    }

    [Fact]
    public void SwbCanUseTheActualParameterlessTextEntryWithAnEmptyModule()
    {
        string source = Source();
        File.WriteAllText(source, "'Sub commented()\nSub main()\nEnd Sub\nSub needsArgument(value As String)\nEnd Sub");
        var entry = Assert.Single(UserMacroStore.ReadTextEntryPoints(File.ReadAllText(source)));
        Assert.Equal("", entry.ModuleName); Assert.Equal("main", entry.ProcedureName); Assert.Equal("main", entry.ToString());
        var command = UserMacroStore.Import(source, "text macro", entry.ModuleName, entry.ProcedureName, null, Storage);
        int calls = 0;
        UserMacroStore.Execute(command, (string path, string module, string procedure, int options, out int error) =>
        { calls++; Assert.Equal("", module); Assert.Equal("main", procedure); error = 0; return true; }, Storage);
        Assert.Equal(1, calls);
        var bare = Assert.Single(UserMacroStore.ParseEntryPoints("main", allowEmptyModule: true));
        Assert.Equal("", bare.ModuleName); Assert.Equal("main", bare.ProcedureName);
        Assert.Empty(UserMacroStore.ReadTextEntryPoints("' there is no procedure"));
    }

    [Fact]
    public void ConfigRoundTripKeepsMacroIdentityAndOldConfigsDefaultToTheProductTab()
    {
        var command = UserMacroStore.Import(Source(), "first", "Module1", "main", null, Storage);
        var config = new PluginConfig { WheelCommandTabEnabled = false, WheelCommandTabName = "我的工具", UserMacros = new List<UserMacroCommand> { command } };
        var restored = ConfigStore.Deserialize(ConfigStore.Serialize(config));
        Assert.False(restored.WheelCommandTabEnabled); Assert.Equal("我的工具", restored.WheelCommandTabName);
        var macro = Assert.Single(restored.UserMacros); Assert.Equal(command.Id, macro.Id); Assert.Equal(command.CommandUserId, macro.CommandUserId);
        Assert.Equal(command.FilePath, macro.FilePath); Assert.Equal(command.ModuleName, macro.ModuleName); Assert.Equal(command.ProcedureName, macro.ProcedureName);
        var old = ConfigStore.Deserialize("{}"); Assert.True(old.WheelCommandTabEnabled); Assert.Equal("XY轮盘", old.WheelCommandTabName); Assert.Empty(old.UserMacros);
    }

    private string Source(string filename = "selected.swb")
    {
        Directory.CreateDirectory(_root); string path = Path.Combine(_root, filename);
        File.WriteAllText(path, "fixture source, never executed by these tests"); return path;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
