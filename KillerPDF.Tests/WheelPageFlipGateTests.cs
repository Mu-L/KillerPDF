using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class WheelPageFlipGateTests
{
    private static readonly DateTime Start = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TwoQuickNotchesAtEdge_ConfirmPageFlip()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-120, Start));
        Assert.True(gate.TryConfirm(-120, Start.AddMilliseconds(100)));
    }

    [Fact]
    public void MomentumImmediatelyAfterContentScroll_DoesNotFlip()
    {
        var gate = new WheelPageFlipGate();
        gate.NoteContentScroll(Start);

        Assert.False(gate.TryConfirm(-120, Start.AddMilliseconds(50)));
        Assert.False(gate.TryConfirm(-120, Start.AddMilliseconds(150)));
    }

    [Fact]
    public void OppositeDirection_RestartsConfirmation()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-120, Start));
        Assert.False(gate.TryConfirm(120, Start.AddMilliseconds(100)));
        Assert.True(gate.TryConfirm(120, Start.AddMilliseconds(200)));
    }

    [Fact]
    public void SlowNotches_DoNotCombineIntoPageFlip()
    {
        var gate = new WheelPageFlipGate();

        Assert.False(gate.TryConfirm(-120, Start));
        Assert.False(gate.TryConfirm(-120, Start.AddMilliseconds(700)));
    }
}
