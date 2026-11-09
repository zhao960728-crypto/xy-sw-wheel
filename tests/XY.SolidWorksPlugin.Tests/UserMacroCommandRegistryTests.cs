using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class UserMacroCommandRegistryTests
{
    [Fact]
    public void ButtonsCarryStableNumericIdentityRatherThanNamePathOrPosition()
    {
        var first = Command(10009, "same source first");
        var second = Command(10023, "user label (other callback)");
        var calls = new List<object[]>();
        var group = (ICommandGroup)new GroupProxy(call =>
        {
            Assert.Equal("AddCommandItem2", call.MethodName);
            calls.Add(call.Args.ToArray()); return calls.Count - 1;
        }).GetTransparentProxy();
        Assert.Equal(new[] { 0, 1 }, UserMacroCommandRegistry.AddItems(group, new[] { second, first }));
        Assert.Equal("RunUserMacroCommand(10023)", calls[0][5]);
        Assert.Equal("RunUserMacroCommandEnable(10023)", calls[0][6]); Assert.Equal(10023, calls[0][7]);
        Assert.Equal("RunUserMacroCommand(10009)", calls[1][5]); Assert.Equal(10009, calls[1][7]);
        Assert.Equal(second.Name, calls[0][0]); Assert.DoesNotContain(second.Name, (string)calls[0][5]);
    }

    [Theory]
    [InlineData("RunUserMacroCommand")]
    [InlineData("RunUserMacroCommandEnable")]
    public void SolidWorksParameterizedCallbacksHaveTheRequiredStringSignature(string method)
    {
        var callback = typeof(SwAddin).GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(callback); Assert.Equal(typeof(string), Assert.Single(callback!.GetParameters()).ParameterType);
    }

    [Fact]
    public void ResolutionKeepsTheRegisteredGuidAndExactEntryAcrossAConfigRoundTrip()
    {
        var first = Command(10009, "first"); var second = Command(10023, "second");
        var restored = ConfigStore.Deserialize(ConfigStore.Serialize(new PluginConfig { UserMacros = new List<UserMacroCommand> { second, first } }));
        var selected = UserMacroCommandRegistry.Resolve(new[] { first, second }, restored.UserMacros, "10009");
        Assert.Equal(first.Id, selected.Id); Assert.Equal(first.FilePath, selected.FilePath);
        Assert.Equal(first.ModuleName, selected.ModuleName); Assert.Equal(first.ProcedureName, selected.ProcedureName);
        Assert.NotSame(first, selected);
    }

    [Fact]
    public void AChangedPathProcedureOrGuidCannotReuseTheRegisteredCallback()
    {
        var original = Command(10009, "test");
        foreach (Action<UserMacroCommand> change in new Action<UserMacroCommand>[]
        {
            command => command.FilePath = "another.swb",
            command => command.ProcedureName = "another",
            command => command.Id = Guid.NewGuid().ToString("N")
        })
        {
            var edited = Command(10009, original.Name); edited.Id = original.Id; edited.FilePath = original.FilePath; change(edited);
            Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.Resolve(new[] { original }, new[] { edited }, "10009"));
        }
    }

    [Fact]
    public void RemovedUnknownAndDuplicateCallbacksAreRejectedInsteadOfFindingAnotherMacro()
    {
        var first = Command(10009, "first"); var second = Command(10023, "second");
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.Resolve(new[] { first, second }, new[] { second }, "10009"));
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.Resolve(new[] { first }, new[] { first, first }, "10009"));
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.Resolve(new[] { first }, new[] { first }, "10009 trailing"));
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.Resolve(new[] { first }, new[] { first }, "10023"));
    }

    [Fact]
    public void RegistrationRefusesDuplicateIdsOrNamesAndInvalidGuids()
    {
        var first = Command(10009, "first"); var second = Command(10009, "second");
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.ValidateDefinitions(new[] { first, second }));
        second.CommandUserId = 10023; second.Name = " FIRST ";
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.ValidateDefinitions(new[] { first, second }));
        second.Name = "second"; second.Id = "invalid";
        Assert.Throws<InvalidOperationException>(() => UserMacroCommandRegistry.ValidateDefinitions(new[] { second }));
    }

    private static UserMacroCommand Command(int id, string name)
    {
        string guid = Guid.NewGuid().ToString("N");
        return new UserMacroCommand { Id = guid, CommandUserId = id, Name = name, FilePath = "owned/" + guid + "/macro.swb", ModuleName = "", ProcedureName = "main" };
    }

    private sealed class GroupProxy : RealProxy
    {
        private readonly Func<IMethodCallMessage, object> _call;
        internal GroupProxy(Func<IMethodCallMessage, object> call) : base(typeof(ICommandGroup)) => _call = call;
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try { return new ReturnMessage(_call(call), call.Args, call.ArgCount, call.LogicalCallContext, call); }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
}
