using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace XY.SolidWorksPlugin.Tests;

public sealed class PurchasedPartMarkerTests
{
    [Theory]
    [InlineData("是", true)] [InlineData("外购件", true)] [InlineData("yes", true)] [InlineData("1", true)]
    [InlineData("否", false)] [InlineData("自制", false)] [InlineData("0", false)] [InlineData("不外购", false)]
    public void FlagHasExplicitMeanings(string text, bool expected) => Assert.Equal(expected, PurchasedPartPolicy.ExplicitFlag(text));

    [Theory]
    [InlineData("")] [InlineData("待定")] [InlineData("供应商待确认")]
    public void ArbitraryTextIsNotTrue(string value) => Assert.Null(PurchasedPartPolicy.ExplicitFlag(value));

    [Theory]
    [InlineData("电机", "")]
    [InlineData("外购气缸", "外购件")]
    [InlineData("M8螺栓", "标准件")]
    public void ExplicitNoOverridesBothSupplyNamesAndSupplyTypes(string title, string type)
    {
        var input = new ProcessRecognitionInput { IsPart = true, DocumentTitle = title, PartType = type, Purchase = "否" };
        Assert.False(PurchasedPartPolicy.IsPurchased(input));
        Assert.False(ProcessRecognitionService.IsDeclaredPurchasedOrStandard(input));
        Assert.DoesNotContain(ProcessRecognitionService.Recognize(input, false, new ProcessRecognitionConfig()), row => row.Name == "外购");
        Assert.Equal(PartTypes.SheetMetal, ProcessRecognitionService.ResolvePartType(input, true, false, false, false, false, false, false).Type);
    }
    [Fact]
    public void PurchasedProfileDoesNotReceiveItsMachiningDefaultGroup()
    {
        var config = new ProcessRecognitionConfig { ProcessCombinations = "方通组=切管>钻孔|采购组=来料检验>入库", ProcessTypeGroups = "方通=方通组" };
        var input = new ProcessRecognitionInput { IsPart = true, PartType = "方通", Purchase = "是" };
        Assert.False(ProcessTypeGroupPolicy.Resolve(config,input).Matched);
        config.ProcessTypeGroups += "|外购件=采购组";
        Assert.Equal("采购组", ProcessTypeGroupPolicy.Resolve(config,input).Group);
        Assert.Equal(new[] { "来料检验", "入库" }, ProcessRecognitionService.Recognize(input, false, config).Select(row=>row.Name));
        input.Purchase = "否"; Assert.Equal("方通组",ProcessTypeGroupPolicy.Resolve(config,input).Group);
        input.PartType = "外购件"; Assert.False(ProcessTypeGroupPolicy.Resolve(config,input).Matched);
    }
    [Fact]
    public void MarkerChangesOnlyItsOnePropertyAndRejectsStalePreview()
    {
        var store = new MemoryStore(); store.Values["工序"]="用户工序"; store.Values["类型"]="钣金件";
        var snapshot = store.Read(PurchasedPartPolicy.PropertyName);
        Assert.Equal(1, PurchasedPartPolicy.Apply(store,snapshot,true));
        Assert.Equal("用户工序",store.Values["工序"]); Assert.Equal("钣金件",store.Values["类型"]);
        Assert.Throws<InvalidOperationException>(()=>PurchasedPartPolicy.Apply(store,snapshot,false));
        Assert.Equal("是",store.Values[PurchasedPartPolicy.PropertyName]);
        Assert.Equal(1, PurchasedPartPolicy.Apply(store,store.Read(PurchasedPartPolicy.PropertyName),false));
        Assert.Equal("否",store.Values[PurchasedPartPolicy.PropertyName]);
        Assert.Equal(3,store.Values.Count);
    }
    [Fact]
    public void FailedMarkerVerificationRestoresPreviousFlag()
    {
        var store = new MemoryStore(); store.Values[PurchasedPartPolicy.PropertyName]="否";
        var snapshot=store.Read(PurchasedPartPolicy.PropertyName); store.Fail=true;
        Assert.Throws<InvalidOperationException>(()=>PurchasedPartPolicy.Apply(store,snapshot,true));
        Assert.Equal("否",store.Values[PurchasedPartPolicy.PropertyName]);
    }
    private sealed class MemoryStore : PropertyWriteBatch.Store
    {
        internal readonly Dictionary<string,string> Values = new Dictionary<string,string>();
        internal bool Fail;
        public PropertyWriteBatch.Snapshot Read(string name) => Values.TryGetValue(name,out string value) ? new PropertyWriteBatch.Snapshot { Exists=true, Raw=value, Resolved=value, Type=30 } : new PropertyWriteBatch.Snapshot();
        public void Set(string name,string value,PropertyWriteBatch.Snapshot previous) {Values[name]=value;if(Fail)throw new InvalidOperationException("模拟回读失败");}
        public void Restore(string name,PropertyWriteBatch.Snapshot previous) {if(previous.Exists)Values[name]=previous.Raw;else Values.Remove(name);}
    }
}
