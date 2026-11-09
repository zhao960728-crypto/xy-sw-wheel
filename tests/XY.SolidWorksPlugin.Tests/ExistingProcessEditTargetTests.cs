using System;
using System.Collections.Generic;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class ExistingProcessEditTargetTests
{
    [Fact]
    public void DirectEditingCanWriteTwiceUsingFreshSnapshotsWithoutChangingAnotherScope()
    {
        var document = new MemoryStore(); document.Values["工序"]="下料-折弯-喷粉"; document.Values["类型"]="钣金件";
        var configuration = new MemoryStore(); configuration.Values["工序"]="另一配置工序";
        var target = new ExistingProcessEditTarget("", "工序", document.Read("工序"));
        Assert.Equal(1, target.Apply(document,"下料-去毛刺-折弯-喷粉"));
        target = new ExistingProcessEditTarget("", "工序", document.Read("工序"));
        Assert.Equal(1, target.Apply(document,"折弯-去毛刺-检验"));
        Assert.Equal("折弯-去毛刺-检验",document.Values["工序"]);
        Assert.Equal("另一配置工序",configuration.Values["工序"]);
        Assert.Equal("钣金件",document.Values["类型"]);
    }
    [Fact]
    public void ExplicitlyStagedGroupIsWrittenAlongsideEditedSequenceButOtherPropertiesStayIntact()
    {
        var store = new MemoryStore(); store.Values["工序"]="下料"; store.Values["工序类型"]="旧组";store.Values["是否外购件"]="否";
        var target = new ExistingProcessEditTarget("ConfigA","工序",store.Read("工序"));
        Assert.Equal(2,target.Apply(store,"切管-钻孔",new[]{new KeyValuePair<string,string>("工序类型","方通组")}));
        Assert.Equal("方通组",store.Values["工序类型"]);Assert.Equal("否",store.Values["是否外购件"]);
        Assert.Equal("ConfigA",target.Configuration);
    }
    [Fact]
    public void SourceChangesRejectOverwriteAndDeliberateClearIsSupported()
    {
        var store=new MemoryStore();store.Values["工序"]="下料-折弯";
        var target=new ExistingProcessEditTarget("配置一","工序",store.Read("工序"));
        store.Values["工序"]="用户刚修改";
        Assert.Throws<InvalidOperationException>(()=>target.Apply(store,"喷粉"));
        Assert.Equal("用户刚修改",store.Values["工序"]);
        target=new ExistingProcessEditTarget("配置一","工序",store.Read("工序"));
        Assert.Equal(1,target.Apply(store,""));Assert.Equal("",store.Values["工序"]);
    }
    [Fact]
    public void ConfirmingUnchangedResolvedExpressionDoesNotFlattenItsRawValue()
    {
        var store=new MemoryStore();store.Values["工序"]="=$PRP:工序模板";store.Resolved="下料-折弯";
        var target=new ExistingProcessEditTarget("","工序",store.Read("工序"));
        Assert.Equal(0,target.Apply(store,"下料-折弯"));
        Assert.Equal("=$PRP:工序模板",store.Values["工序"]);
    }
    private sealed class MemoryStore : PropertyWriteBatch.Store
    {
        internal Dictionary<string,string> Values=new Dictionary<string,string>(); internal string? Resolved;
        public PropertyWriteBatch.Snapshot Read(string name)=>Values.TryGetValue(name,out string value)?new PropertyWriteBatch.Snapshot{Exists=true,Raw=value,Resolved=Resolved??value,Type=30}:new PropertyWriteBatch.Snapshot();
        public void Set(string name,string value,PropertyWriteBatch.Snapshot previous){Values[name]=value;}
        public void Restore(string name,PropertyWriteBatch.Snapshot previous){if(previous.Exists)Values[name]=previous.Raw;else Values.Remove(name);}
    }
}
