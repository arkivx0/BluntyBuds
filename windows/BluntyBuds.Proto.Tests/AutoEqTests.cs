using BluntyBuds.Proto;
using Xunit;

public class AutoEqTests
{
    static string Fixture(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "autoeq", n));
    static float[] F(params float[] x) => x;

    [Fact] public void ParsesParametric()
    {
        var fl = AutoEq.ParseParametric(Fixture("NC9 ParametricEQ.txt"));
        Assert.Equal(10, fl.Count);
        Assert.Equal(new AutoEq.Filter("LS", 105, -5.2, 0.70), fl[0]);
        Assert.Equal(new AutoEq.Filter("HS", 10000, -13.4, 0.70), fl[5]);
        Assert.Equal("PK", fl[1].Kind);
    }
    [Fact] public void ParametricSkipsOffAndUnknownAndThrowsOnEmpty()
    {
        var fl = AutoEq.ParseParametric("Preamp: -3 dB\r\nFilter 1: OFF PK Fc 100 Hz Gain 9 dB Q 1\r\nFilter 2: ON XX Fc 1 Hz Gain 1 dB Q 1\r\nFilter 3: ON PK Fc 800 Hz Gain 6 dB Q 1.2\r\n");
        Assert.Single(fl);
        Assert.Throws<FormatException>(() => AutoEq.ParseParametric("hello"));
    }
    [Fact] public void ParsesGraphicAndInterpolates()
    {
        var p = AutoEq.ParseGraphic("GraphicEQ: 1000 2; 100 0; 10000 -2");
        Assert.Equal(3, p.Count); Assert.Equal(100, p[0].F);
        Assert.Equal(1, AutoEq.GraphicAt(p, Math.Sqrt(100 * 1000)), 6);
        Assert.Equal(0, AutoEq.GraphicAt(p, 20)); Assert.Equal(-2, AutoEq.GraphicAt(p, 20000));
        Assert.Throws<FormatException>(() => AutoEq.ParseGraphic("Filter 1: ON PK"));
    }
    [Fact] public void BiquadPeakAtCentre() => Assert.Equal(6, AutoEq.BiquadDb("PK", 1000, 6, 1, 1000), 3);
    [Fact] public void Quantizes()
    {
        Assert.Equal(0.5f, AutoEq.Quantize(0.25)); Assert.Equal(0f, AutoEq.Quantize(-0.25)); Assert.Equal(-0.5f, AutoEq.Quantize(-0.26));
        Assert.Equal(12f, AutoEq.Quantize(13)); Assert.Equal(-12f, AutoEq.Quantize(-40));
    }
    // Expected values come from the reference implementation tools/autoeq/autoeq_fit.py.
    [Fact] public void FitsFlat() => Assert.Equal(new float[10], AutoEq.FitText("GraphicEQ: 20 0; 1000 0; 20000 0"));
    [Fact] public void FitsSinglePeak() => Assert.Equal(F(-6.5f, -5, -5.5f, -5.5f, -5.5f, 0, -6, -6.5f, -6.5f, -7),
        AutoEq.FitText("Preamp: -3 dB\nFilter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\nFilter 2: OFF PK Fc 100 Hz Gain 9 dB Q 1\n"));
    [Fact] public void FitClampsToRange() => Assert.Equal(F(0, 0, -4.5f, -12, -12, -12, -12, -12, -12, -12),
        AutoEq.FitText("Preamp: 0 dB\r\nFilter 1: ON LSC Fc 105 Hz Gain 30 dB Q 0.7\r\n"));
    [Fact] public void FitsParametricFixture() => Assert.Equal(F(-10.5f, -6.5f, -4.5f, -7.5f, -3.5f, 0, -4.5f, -9, -7, -12), AutoEq.FitText(Fixture("NC9 ParametricEQ.txt")));
    [Fact] public void GraphicFixtureMatchesBuiltInProfile() => Assert.Equal(AutoEq.For("NC9")!.Gains, AutoEq.FitText(Fixture("NC9 GraphicEQ.txt")));
    [Fact] public void FitWithoutPreampUsesMeanThenHeadroom() => Assert.Equal(F(-6, -5.5f, -6, -6, -6, 0, -6, -6, -6, -6.5f),
        AutoEq.FitText("Filter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\n"));
    [Fact] public void FitHonorsLowPreamp() => Assert.Equal(F(-10, -4, -5, -6, -6.5f, -1, -8, -8.5f, -9.5f, -11),
        AutoEq.FitText("Preamp: -12 dB\nFilter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\n"));
    [Fact] public void ParsesPreamp()
    {
        Assert.Equal(-5.6, AutoEq.ParsePreamp(Fixture("NC9 ParametricEQ.txt")));
        Assert.Null(AutoEq.ParsePreamp("Filter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2"));
    }
    [Fact] public void HeadroomShiftsDownOnlyWhenBoosting()
    {
        Assert.Equal(new double[] { -3, 0, -5 }, AutoEq.Headroom(new double[] { 2, 5, 0 }));
        Assert.Equal(new double[] { -1, -2 }, AutoEq.Headroom(new double[] { -1, -2 }));
    }
    [Fact] public void ImportedFilesNeverBoost()
    {
        foreach (var t in new[] { Fixture("NC9 ParametricEQ.txt"), Fixture("NC9 GraphicEQ.txt"), "Preamp: 6 dB\nFilter 1: ON PK Fc 100 Hz Gain 9 dB Q 1\n", "GraphicEQ: 20 10; 1000 12; 20000 8" })
            Assert.True(AutoEq.FitText(t).Max() <= 0);
    }
    [Fact] public void SuggestOnlyConfidentForMatchingChip()
    {
        var ps = new List<AutoEq.Profile> { new("NC9", "m", new float[10], "BES"), new("T6", "m", new float[10]) };
        var bes = Models.ByMark("1A0E")!; var wuqi = Models.ByMark("6B")!;
        Assert.True(AutoEq.Suggest(bes, ps).Confident);
        Assert.False(AutoEq.Suggest(wuqi, ps).Confident); Assert.NotNull(AutoEq.Suggest(wuqi, ps).Profile);
        Assert.False(AutoEq.Suggest(Models.Detect("TOZO NC9"), ps).Confident);   // name only: revision unknown
        Assert.False(AutoEq.Suggest(Models.ByMark("86")!, ps).Confident);         // measured chip unknown
        Assert.Null(AutoEq.Suggest(DeviceModel.Unknown, ps).Profile);
        // Built-in table: no measured chip is known yet, so nothing is auto-selected
        Assert.False(AutoEq.Suggest(bes).Confident);
        Assert.Equal("AutoEQ · NC9 (may differ by hardware revision)", AutoEq.SuggestLabel(ps[0], false));
        Assert.Equal("AutoEQ · NC9 (this model)", AutoEq.SuggestLabel(ps[0], true));
    }
    [Fact] public void BuiltInProfiles()
    {
        Assert.True(AutoEq.Profiles.Count >= 1);
        Assert.All(AutoEq.Profiles, p => { Assert.Equal(10, p.Gains.Length); Assert.All(p.Gains, g => Assert.InRange(g, -12f, 0f)); Assert.Equal(0f, p.Gains.Max()); });
        Assert.NotNull(AutoEq.For("nc9")); Assert.Null(AutoEq.For("NC9Pro")); Assert.Null(AutoEq.For(null));
    }
    [Fact] public void ParsesProfileTsv()
    {
        var p = AutoEq.ParseProfiles("# c\nX1\tsrc\t1,2,3,4,5,6,7,8,9,10\r\nbad line\nY\ts\t1,2\n");
        Assert.Single(p); Assert.Equal("X1", p[0].Model); Assert.Equal(10f, p[0].Gains[9]); Assert.Null(p[0].Chip);
        Assert.Equal("BES", AutoEq.ParseProfiles("A\ts\t1,2,3,4,5,6,7,8,9,10\tBES\n")[0].Chip);
    }
}
