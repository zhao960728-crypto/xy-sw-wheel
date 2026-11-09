using System;
using System.Drawing;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ProcessRecognitionTaskPaneHostTests
{
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [Fact]
    public void NativeReparentingFillsInitialClientAreaAndTracksResizeWithoutManagedParent() => Sta(() =>
    {
        using var parent = new Panel { Size = new Size(373, 770) };
        using var child = new UserControl();
        SetParent(child.Handle, parent.Handle);
        Assert.Null(child.Parent);
        Assert.Equal(new Size(150, 150), child.Size);
        using (var sizing = new NativePaneSizing(child, parent.Handle))
        {
            Assert.Equal(parent.ClientRectangle, child.Bounds);
            parent.Size = new Size(460, 690);
            Assert.Equal(parent.ClientRectangle, child.Bounds);
        }
        parent.Size = new Size(510, 710);
        Assert.Equal(new Size(460, 690), child.Size);
        SetParent(child.Handle, IntPtr.Zero);
    });

    [Fact]
    public void UnattachedParkingWindowIsNotUsedAsPaneSizingParent() => Sta(() =>
    {
        using var child = new UserControl();
        Assert.Equal(IntPtr.Zero, NativePaneSizing.ParentOf(child));
    });

    private sealed class View : ProcessRecognitionTaskPaneHost.IView
    {
        internal bool DisplayResult = true, ShowResult = true, DeleteResult = true;
        internal int Displays, Shows, Hides, Deletes;
        internal bool Released;
        public bool Display(IntPtr handle) { Assert.NotEqual(IntPtr.Zero, handle); Displays++; return DisplayResult; }
        public bool Show() { Shows++; return ShowResult; }
        public bool Hide() { Hides++; return true; }
        public bool Delete() { Deletes++; return DeleteResult; }
        public void Dispose() { Released = true; }
    }

    [Fact]
    public void BorrowedFormBecomesResponsiveAndDetachRestoresItWithoutDestroyingDraft() => Sta(() =>
    {
        var view = new View(); using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var form = new Form { MinimumSize = new Size(1010, 700), Size = new Size(1010, 700) };
        using var draft = new TextBox { Text = "未写入工序草稿" }; form.Controls.Add(draft);
        host.Attach(form);
        Assert.False(form.TopLevel); Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
        Assert.Equal(Size.Empty, form.MinimumSize); Assert.Equal(DockStyle.Fill, form.Dock);
        Assert.IsType<UserControl>(form.Parent); Assert.Same(form, host.Content);
        Control surface = form.Parent!;
        surface.Size = new Size(300, 450); surface.PerformLayout();
        Assert.Equal(surface.ClientSize, form.Size);
        Assert.Same(form, host.Detach());
        Assert.True(form.TopLevel); Assert.Equal(new Size(1010, 700), form.MinimumSize);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.Null(form.Parent); Assert.False(form.Visible); Assert.False(form.IsDisposed);
        Assert.Equal("未写入工序草稿", draft.Text);
        Assert.Equal(1, view.Hides);
    });

    [Fact]
    public void SwitchingContentReusesNativeTabAndPreservesBothForms() => Sta(() =>
    {
        var view = new View(); using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var first = new Form(); using var second = new Form();
        host.Attach(first); host.Attach(second); host.Attach(second);
        Assert.Equal(1, view.Displays); Assert.Equal(3, view.Shows);
        Assert.True(first.TopLevel); Assert.False(first.Visible); Assert.False(first.IsDisposed);
        Assert.Same(second, host.Content);
        host.Dispose();
        Assert.True(second.TopLevel); Assert.False(second.Visible); Assert.False(second.IsDisposed);
        Assert.Equal(1, view.Deletes); Assert.True(view.Released);
    });

    [Fact]
    public void EmbedFailureRemovesOwnedTabAndDoesNotMutateContent() => Sta(() =>
    {
        var view = new View { DisplayResult = false };
        using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var form = new Form();
        Assert.Throws<InvalidOperationException>(() => host.Attach(form));
        Assert.False(host.IsCreated); Assert.Equal(1, view.Deletes); Assert.True(view.Released);
        Assert.True(form.TopLevel); Assert.False(form.IsDisposed);
    });

    [Fact]
    public void ShowFailureRestoresFormAndReleasesNewTab() => Sta(() =>
    {
        var view = new View { ShowResult = false };
        using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var form = new Form { MinimumSize = new Size(900, 600) };
        Assert.Throws<InvalidOperationException>(() => host.Attach(form));
        Assert.True(form.TopLevel); Assert.Equal(new Size(900, 600), form.MinimumSize);
        Assert.Null(form.Parent); Assert.False(form.IsDisposed); Assert.True(view.Released);
    });

    [Fact]
    public void ExistingTabShowFailureKeepsPreviousDraftAttached() => Sta(() =>
    {
        var view = new View(); using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var first = new Form(); using var second = new Form();
        host.Attach(first); view.ShowResult = false;
        Assert.Throws<InvalidOperationException>(() => host.Attach(second));
        Assert.Same(first, host.Content); Assert.False(first.TopLevel); Assert.NotNull(first.Parent);
        Assert.True(second.TopLevel); Assert.Null(second.Parent); Assert.Equal(0, view.Deletes);
    });

    [Fact]
    public void FailedNativeDeletionKeepsTheHandleAliveForRetryWithoutDestroyingContent() => Sta(() =>
    {
        var view = new View(); var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var form = new Form(); host.Attach(form); Control surface = form.Parent!;
        view.DeleteResult = false;
        Assert.Throws<InvalidOperationException>(() => host.Dispose());
        Assert.False(surface.IsDisposed); Assert.True(host.IsCreated); Assert.False(view.Released);
        Assert.False(form.IsDisposed); Assert.True(form.TopLevel);
        view.DeleteResult = true; host.Dispose();
        Assert.True(surface.IsDisposed); Assert.True(view.Released); Assert.False(form.IsDisposed);
    });

    [Fact]
    public void EmbeddedContentCanOpenAndCompleteAModelessEditor() => Sta(() =>
    {
        var view = new View(); using var host = new ProcessRecognitionTaskPaneHost(() => view);
        using var form = new PluginToolWindow(); host.Attach(form);
        using var editor = new PluginToolWindow();
        DialogResult? result = null;
        editor.ShowModeless(form, value => result = value);
        Assert.True(editor.Visible); Assert.True(editor.TopLevel);
        editor.DialogResult = DialogResult.Cancel; editor.Close();
        Assert.Equal(DialogResult.Cancel, result);
        Assert.Same(form, host.Content); Assert.False(form.IsDisposed);
    });

    [Fact]
    public void CallerDisposalHidesEmptyTabAndOtherThreadsCannotTouchCom() => Sta(() =>
    {
        var view = new View(); using var host = new ProcessRecognitionTaskPaneHost(() => view);
        var form = new Form(); host.Attach(form); form.Dispose();
        Assert.Null(host.Content); Assert.Equal(1, view.Hides);
        Exception? failure = null;
        var thread = new Thread(() => { try { host.Hide(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(failure); Assert.Equal(1, view.Hides);
    });

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
