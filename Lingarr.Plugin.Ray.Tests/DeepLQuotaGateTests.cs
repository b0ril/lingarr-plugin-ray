using Lingarr.Plugin.Ray.DeepL;
using Xunit;

namespace Lingarr.Plugin.Ray.Tests;

public class DeepLQuotaGateTests
{
    [Fact]
    public void MarkExhausted_SkipsDeepLUntilCleared()
    {
        var gate = new DeepLQuotaGate();
        Assert.False(gate.IsExhausted);

        gate.MarkExhausted();
        Assert.True(gate.IsExhausted);

        gate.Clear();
        Assert.False(gate.IsExhausted);
    }

    [Fact]
    public void RefreshFromUsage_ClearsWhenLimitNotReached()
    {
        var gate = new DeepLQuotaGate();
        gate.MarkExhausted();

        gate.RefreshFromUsage(anyLimitReached: false);
        Assert.False(gate.IsExhausted);
    }

    [Fact]
    public void RefreshFromUsage_KeepsExhaustedWhenLimitReached()
    {
        var gate = new DeepLQuotaGate();
        gate.MarkExhausted();

        gate.RefreshFromUsage(anyLimitReached: true);
        Assert.True(gate.IsExhausted);
    }
}
