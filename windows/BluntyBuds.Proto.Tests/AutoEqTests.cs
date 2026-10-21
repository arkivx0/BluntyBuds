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
    [Fact] public void FitsSinglePeak() => Assert.Equal(F(-1, -0.5f, -0.5f, -0.5f, -0.5f, 5.5f, -0.5f, -0.5f, -1, -1),
        AutoEq.FitText("Preamp: -3 dB\nFilter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\nFilter 2: OFF PK Fc 100 Hz Gain 9 dB Q 1\n"));
    [Fact] public void FitClampsToRange() => Assert.Equal(F(12, 12, 1.5f, -4, -5, -5, -5, -5.5f, -6.5f, -7),
        AutoEq.FitText("Preamp: 0 dB\r\nFilter 1: ON LSC Fc 105 Hz Gain 30 dB Q 0.7\r\n"));
    [Fact] public void FitsParametricFixture() => Assert.Equal(F(-2.5f, -2, 0.5f, -1.5f, 2.5f, 7, 2.5f, -1.5f, 1.5f, -8.5f), AutoEq.FitText(Fixture("NC9 ParametricEQ.txt")));
    [Fact] public void GraphicFixtureMatchesBuiltInProfile() => Assert.Equal(AutoEq.For("NC9")!.Gains, AutoEq.FitText(Fixture("NC9 GraphicEQ.txt")));
    [Fact] public void BuiltInProfiles()
    {
        Assert.True(AutoEq.Profiles.Count >= 1);
        Assert.All(AutoEq.Profiles, p => { Assert.Equal(10, p.Gains.Length); Assert.All(p.Gains, g => Assert.InRange(g, -12f, 12f)); });
        Assert.NotNull(AutoEq.For("nc9")); Assert.Null(AutoEq.For("NC9Pro")); Assert.Null(AutoEq.For(null));
    }
    [Fact] public void ParsesProfileTsv()
    {
        var p = AutoEq.ParseProfiles("# c\nX1\tsrc\t1,2,3,4,5,6,7,8,9,10\r\nbad line\nY\ts\t1,2\n");
        Assert.Single(p); Assert.Equal("X1", p[0].Model); Assert.Equal(10f, p[0].Gains[9]);
    }
}
