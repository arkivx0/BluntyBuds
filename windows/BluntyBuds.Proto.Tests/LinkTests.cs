using BluntyBuds.Proto;
using Xunit;

public class LinkTests
{
    [Fact] public void Backoff_Steps_And_Cap()
    {
        var b = new Backoff(15, 30, 60);
        Assert.Equal(15, b.Next().TotalSeconds); Assert.Equal(30, b.Next().TotalSeconds);
        Assert.Equal(60, b.Next().TotalSeconds); Assert.Equal(60, b.Next().TotalSeconds);
        b.Reset(); Assert.Equal(15, b.Next().TotalSeconds);
        Assert.True(b.ConnectTimeout > b.Max); // a connect attempt outlives the longest retry gap
    }

    [Fact] public void Coalescer_At_Most_Twice_Per_Second()
    {
        var c = new Coalescer(TimeSpan.FromMilliseconds(500));
        var t = new DateTime(2026, 1, 1);
        Assert.False(c.ShouldFlush(t));               // nothing pending
        int flushes = 0;
        for (int ms = 0; ms < 1000; ms += 10) { c.Mark(); if (c.ShouldFlush(t.AddMilliseconds(ms))) flushes++; }
        Assert.Equal(2, flushes);                      // 100 adverts in 1 s -> 2 renders
        c.Mark(); Assert.True(c.ShouldFlush(t.AddMilliseconds(1500)));
    }

    [Fact] public void Generation_Stale_Timer_Ignored()
    {
        var g = new Generation();
        var first = g.Begin(); var second = g.Begin();
        Assert.False(g.IsCurrent(first)); Assert.True(g.IsCurrent(second));
    }

    [Fact] public void Gatt_Error_Names()
    {
        Assert.Equal("insufficient authentication", GattErrors.Name(0x05));
        Assert.Contains("generic GATT error", GattErrors.Name(133));
        Assert.Equal("code 250", GattErrors.Name(250));
    }
}
