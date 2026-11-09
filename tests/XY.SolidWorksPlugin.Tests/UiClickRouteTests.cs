using System;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class UiClickRouteTests
{
    [Fact]
    public void TextStepRejectsDuplicateButtonsAndNeverChoosesFirst()
    {
        Sta(() =>
        {
            using var form = new Form { ShowInTaskbar = false };
            int clicked = 0;
            var first = new Button { Text = "确定", Top = 10 }; var second = new Button { Text = "确定", Top = 50 };
            first.Click += (_, __) => clicked++; second.Click += (_, __) => clicked++;
            form.Controls.AddRange(new Control[] { first, second }); form.Show(); Application.DoEvents();
            Assert.False(UiControlClicker.ClickUniqueByText(form.Handle, "确定", TimeSpan.Zero, CancellationToken.None, out string message));
            Assert.Contains("多个同名", message); Assert.Equal(0, clicked);
        });
    }

    [Fact]
    public void TextStepUsesOnlyExactUniqueCaptionInsideSpecifiedWindow()
    {
        Sta(() =>
        {
            using var form = new Form { ShowInTaskbar = false };
            using var other = new Form { ShowInTaskbar = false };
            int clicked = 0, wrong = 0;
            var exact = new Button { Text = "确定", Top = 10 }; var partial = new Button { Text = "确定全部", Top = 50 }; var outside = new Button { Text = "确定" };
            exact.Click += (_, __) => clicked++; partial.Click += (_, __) => wrong++; outside.Click += (_, __) => wrong++;
            form.Controls.AddRange(new Control[] { exact, partial }); other.Controls.Add(outside);
            form.Show(); other.Show(); Application.DoEvents();
            Assert.True(UiControlClicker.ClickUniqueByText(form.Handle, "确定", TimeSpan.Zero, CancellationToken.None, out _));
            Assert.Equal(1, clicked); Assert.Equal(0, wrong);
        });
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI route test timed out.");
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
