using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class PropertyIoScopeTests
{
    [Fact]
    public void CalculatedMassUsesTheInheritedComInterfaceProperty()
    {
        var mass = Proxy<MassProperty>(call => call.MethodName == "get_Mass" ? (object)0.125 : null);
        Assert.Null(typeof(MassProperty).GetProperty("Mass"));
        var extension = Proxy<ModelDocExtension>(call => call.MethodName == "CreateMassProperty" ? mass : null);
        var document = Proxy<IModelDoc2>(call => call.MethodName == "get_Extension" ? extension : null);
        Assert.Equal("0.125", PropertyIo.Read(document, "原生属性", "质量"));
        Assert.Equal("0.125", PropertyIo.Read(document, "原生属性", "重量"));
    }

    [Fact]
    public void InheritedComNameIsReadThroughItsInterfaceAcrossConfigurationChanges()
    {
        var fixture = new Fixture();
        // The interop Configuration interface inherits Name from IConfiguration;
        // Type.GetProperty does not search that inherited interface.
        Assert.Null(typeof(Configuration).GetProperty("Name"));
        Assert.Equal("A", ((IConfiguration)fixture.Configuration).Name);
        foreach (string scope in new[] { "A", "B", "A" })
        {
            fixture.ActiveConfiguration = scope;
            Assert.Equal(fixture.Route(scope), PropertyIo.Read(fixture.Document, "工序"));
            Assert.Contains("仅" + scope, PropertyIo.Names(fixture.Document));
            Assert.DoesNotContain("仅" + (scope == "A" ? "B" : "A"), PropertyIo.Names(fixture.Document));
        }
    }

    [Fact]
    public void NativeReaderRetainsEachManualRouteAndRepeatedInspectionAcrossA_B_A()
    {
        var fixture = new Fixture();
        var config = new ProcessRecognitionConfig();
        foreach (string scope in new[] { "A", "B", "A" })
        {
            fixture.ActiveConfiguration = scope;
            ProcessRecognitionInput input = SolidWorksProcessFeatureReader.Read(fixture.Document, true, config);
            Assert.Equal(fixture.Route(scope), input.ExistingProcess);
            Assert.Equal(fixture.Route(scope).Split('-'), ProcessRecognitionService.Recognize(input, true, config).Select(row => row.Name));
            Assert.Equal(scope, fixture.ActiveConfiguration);
        }
    }

    [Fact]
    public void UnavailableActiveConfigurationIsNotAnExtraDocumentLevelManager()
    {
        var fixture = new Fixture { ActiveConfiguration = "" };
        Assert.Equal(fixture.Route(""), PropertyIo.Read(fixture.Document, "工序"));
        Assert.Equal(new[] { "" }, fixture.ManagerScopes);
    }

    private sealed class Fixture
    {
        private readonly Dictionary<string, Dictionary<string, string>> _properties = new Dictionary<string, Dictionary<string, string>>
        {
            [""] = new Dictionary<string, string> { ["工序"] = "文档下料-文档检验" },
            ["A"] = new Dictionary<string, string> { ["工序"] = "来料检验-人工修配-来料检验", ["仅A"] = "A" },
            ["B"] = new Dictionary<string, string> { ["工序"] = "手工装配-终检", ["仅B"] = "B" }
        };
        internal string ActiveConfiguration = "A";
        internal readonly List<string> ManagerScopes = new List<string>();
        internal readonly Configuration Configuration;
        internal readonly IModelDoc2 Document;
        internal string Route(string scope) => _properties[scope]["工序"];
        internal Fixture()
        {
            Configuration = Proxy<Configuration>(call => call.MethodName == "get_Name" ? ActiveConfiguration : null);
            var configurations = Proxy<ConfigurationManager>(call => call.MethodName == "get_ActiveConfiguration" ? Configuration : null);
            var extension = Proxy<ModelDocExtension>(call =>
            {
                if (call.MethodName != "get_CustomPropertyManager") return null;
                string scope = (string)call.Args[0];
                ManagerScopes.Add(scope);
                return Manager(scope);
            });
            Document = Proxy<IModelDoc2>(call => call.MethodName switch
            {
                "GetType" => 2, "GetTitle" => "ConfigurationAudit", "get_ConfigurationManager" => configurations,
                "get_Extension" => extension, _ => null
            });
        }
        private ICustomPropertyManager Manager(string scope) => Proxy<CustomPropertyManager>(call =>
        {
            var values = _properties[scope];
            if (call.MethodName == "GetNames") return values.Keys.Cast<object>().ToArray();
            if (call.MethodName == "Get4") return new Value { Text = values.TryGetValue((string)call.Args[0], out var value) ? value : "" };
            return null;
        });
    }
    private sealed class Value { internal string Text = ""; }
    private static T Proxy<T>(Func<IMethodCallMessage, object?> invoke) => (T)new FakeCom(typeof(T), invoke).GetTransparentProxy();
    private sealed class FakeCom : RealProxy
    {
        private readonly Func<IMethodCallMessage, object?> _invoke;
        internal FakeCom(Type type, Func<IMethodCallMessage, object?> invoke) : base(type) => _invoke = invoke;
        public override IMessage Invoke(IMessage message)
        {
            var call = (IMethodCallMessage)message;
            try
            {
                object? value = _invoke(call);
                var args = call.Args;
                if (value is Value property) { args[2] = property.Text; args[3] = property.Text; value = true; }
                return new ReturnMessage(value, args, args.Length, call.LogicalCallContext, call);
            }
            catch (Exception error) { return new ReturnMessage(error, call); }
        }
    }
}
