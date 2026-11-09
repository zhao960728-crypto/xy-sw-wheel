using System;
using System.Linq;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class DocumentRecognitionSessionsTests
{
    private sealed class Document { public override string ToString() => "未命名零件"; }
    private sealed class Draft { public string Process = "人工折弯>焊接"; }
    [Fact]
    public void MoreThanTwentyOpenDocumentsAndEachConfigurationKeepIndependentDrafts()
    {
        var sessions = new DocumentRecognitionSessions<Draft>(); var documents = Enumerable.Range(0, 35).Select(_ => new Document()).ToArray();
        foreach (var document in documents) { sessions.Put(document, "默认", new Draft()); sessions.Put(document, "加工", new Draft { Process = "下料>压铆>折弯" }); }
        Assert.Equal(70, sessions.Count);
        foreach (var document in documents)
        {
            Assert.True(sessions.TryGet(document, "默认", out var standard)); Assert.Equal("人工折弯>焊接", standard.Process);
            Assert.True(sessions.TryGet(document, "加工", out var manufacture)); Assert.Equal("下料>压铆>折弯", manufacture.Process);
            Assert.NotSame(standard, manufacture);
        }
        Assert.False(sessions.TryGet(new Document(), "默认", out _));
    }
    [Fact]
    public void ReplacingResultOnlyChangesExactDocumentConfigurationAndClosingReleasesThoseSessions()
    {
        var sessions = new DocumentRecognitionSessions<Draft>(); var first = new Document(); var second = new Document();
        var before = new Draft(); var after = new Draft { Process = "终检" }; var unrelated = new Draft { Process = "包装" };
        sessions.Put(first, "Default", before); sessions.Put(second, "Default", unrelated); sessions.Put(first, "Default", after);
        Assert.Equal(2, sessions.Count); Assert.True(sessions.TryGet(first, "default", out var returned)); Assert.Same(after, returned);
        var closed = sessions.RetainOpenDocuments(new object[] { second }); Assert.Same(after, Assert.Single(closed));
        Assert.False(sessions.TryGet(first, "Default", out _)); Assert.True(sessions.TryGet(second, "Default", out var preserved)); Assert.Same(unrelated, preserved);
        Assert.Equal(1, sessions.Count); sessions.Clear(); Assert.Empty(sessions.Values);
    }
}
