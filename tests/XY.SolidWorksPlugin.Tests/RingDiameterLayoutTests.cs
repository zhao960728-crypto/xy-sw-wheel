using System;
using System.Collections.Generic;
using System.Linq;
using XY.SolidWorksPlugin;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public class RingDiameterLayoutTests
{
    private static SceneMenuConfig Scene(int size, int centerRadius, params int[] slots)
    {
        var scene = new SceneMenuConfig
        {
            MenuSizePx = size,
            CenterRadiusPx = centerRadius,
            StyleId = "01",
            Rings = new List<RingConfig>()
        };
        for (int i = 0; i < slots.Length; i++)
        {
            scene.Rings.Add(new RingConfig { Index = i, Slots = slots[i], Enabled = true });
        }
        return scene;
    }

    // Radial width of each ring: outer radius minus the ring inside it, with
    // the innermost measured from the hub the renderer actually leaves open.
    private static List<double> Bands(SceneMenuConfig scene)
    {
        int wheelDiameter = Math.Max(260, Math.Min(900, scene.MenuSizePx));
        double hub = RingDiameterLayout.EffectiveCenterRadius(scene, wheelDiameter);
        var rings = scene.Rings.Where(r => r.Enabled).OrderBy(r => r.Index).ToList();
        var bands = new List<double>();
        double inner = hub;
        foreach (RingConfig ring in rings)
        {
            double outer = ring.DiameterPx / 2.0;
            bands.Add(outer - inner);
            inner = outer;
        }
        return bands;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryRingGetsTheSameBandWidthWhateverTheRingCount(int count)
    {
        SceneMenuConfig scene = Scene(550, 24, Enumerable.Repeat(8, count).ToArray());
        RingDiameterLayout.Normalize(scene);

        List<double> bands = Bands(scene);
        Assert.Equal(count, bands.Count);
        // A pixel of rounding is allowed; anything more is visible as a ring
        // that is thicker or thinner than its neighbours.
        Assert.True(bands.Max() - bands.Min() <= 1.5,
            "bands were " + string.Join(", ", bands.Select(b => b.ToString("0.##"))));
    }

    [Fact]
    public void TheFirstRingIsNoThinnerThanTheOthers()
    {
        // The regression: the hub was measured from ring 1 after the fact, so
        // the extra hub width came out of the innermost ring alone.
        SceneMenuConfig scene = Scene(550, 24, 8, 12, 16);
        RingDiameterLayout.Normalize(scene);

        List<double> bands = Bands(scene);
        Assert.True(bands[0] >= bands[1] - 1.5,
            "first band " + bands[0].ToString("0.##") + " vs " + string.Join(", ", bands.Select(b => b.ToString("0.##"))));
    }

    [Fact]
    public void RingsStillFillTheWheelFromTheHubToTheEdge()
    {
        SceneMenuConfig scene = Scene(400, 24, 8, 8);
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(400, scene.Rings.OrderBy(r => r.Index).Last().DiameterPx);
        int hubDiameter = RingDiameterLayout.EffectiveCenterDiameter(scene, 400);
        Assert.True(scene.Rings.OrderBy(r => r.Index).First().DiameterPx > hubDiameter);
    }

    [Fact]
    public void ASingleRingKeepsTheSmallConfiguredHub()
    {
        // One ring used to swallow the rim by pushing the hub to absorb the
        // space the ring did not use. The CAD wheel does not do this: the
        // hub stays at the configured radius, the ring band stretches to
        // the rim, and the cells in that band are arc-limited.
        SceneMenuConfig scene = Scene(600, 24, 12);
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(24, RingDiameterLayout.EffectiveCenterRadius(scene, 600));
        Assert.Equal(600, scene.Rings[0].DiameterPx);
        // The ring now runs from the configured hub (24) to the rim (300),
        // so the band is the full 276 px - that is what cells arc-limit
        // themselves against.
        Assert.Equal(276, Bands(scene)[0], 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void HubStaysAtTheConfiguredRadiusRegardlessOfRingCount(int count)
    {
        // A scene with a small configured hub must keep that hub whether it
        // draws one ring or four. The ring count changes band widths, not
        // the size of the neutral cancel target.
        int diameter = 600;
        SceneMenuConfig scene = Scene(diameter, 24, Enumerable.Repeat(8, count).ToArray());
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(24, RingDiameterLayout.EffectiveCenterRadius(scene, diameter));
        // Equally important: every ring band is now the same width.
        List<double> bands = Bands(scene);
        Assert.True(bands.Max() - bands.Min() <= 1.5,
            "bands were " + string.Join(", ", bands.Select(b => b.ToString("0.##"))));
    }

    [Fact]
    public void SeveralRingsShareTheRimBandEvenly()
    {
        SceneMenuConfig scene = Scene(600, 24, 8, 12);
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(24, RingDiameterLayout.EffectiveCenterRadius(scene, 600));
        // The 276 px between hub and rim is split between two rings.
        Assert.Equal(138, Bands(scene)[0], 1);
        Assert.Equal(138, Bands(scene)[1], 1);
    }

    [Fact]
    public void HandTunedDiametersSurviveWhenEqualBandsAreSwitchedOff()
    {
        SceneMenuConfig scene = Scene(400, 24, 8, 8);
        scene.EqualRingBands = false;
        scene.Rings[0].DiameterPx = 300;
        scene.Rings[1].DiameterPx = 400;
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(300, scene.Rings[0].DiameterPx);
        Assert.Equal(400, scene.Rings[1].DiameterPx);
    }

    [Fact]
    public void AddingARingRebalancesEveryRingInsteadOfSplittingOnlyTheOuterOne()
    {
        SceneMenuConfig scene = Scene(550, 24, 8);
        RingDiameterLayout.Normalize(scene);
        scene.Rings.Add(new RingConfig { Index = 1, Slots = 12, Enabled = true });

        RingDiameterLayout.SplitOutermostRing(scene, 1);

        List<double> bands = Bands(scene);
        Assert.True(bands.Max() - bands.Min() <= 1.5,
            "bands were " + string.Join(", ", bands.Select(b => b.ToString("0.##"))));
    }

    [Fact]
    public void NonIconStylesUseTheConfiguredHubAndStillShareTheWidth()
    {
        SceneMenuConfig scene = Scene(520, 44, 8, 8, 8);
        scene.StyleId = "classic";
        RingDiameterLayout.Normalize(scene);

        Assert.Equal(44, RingDiameterLayout.EffectiveCenterRadius(scene, 520));
        List<double> bands = Bands(scene);
        Assert.True(bands.Max() - bands.Min() <= 1.5,
            "bands were " + string.Join(", ", bands.Select(b => b.ToString("0.##"))));
    }
}
