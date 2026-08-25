using System.Windows.Input;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class EditableTextShortcutPolicyTests
{
    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.C)]
    [InlineData(Key.V)]
    [InlineData(Key.X)]
    [InlineData(Key.Z)]
    [InlineData(Key.Y)]
    [InlineData(Key.Left)]
    [InlineData(Key.Delete)]
    public void StandardControlTextGesturesStayInTextBox(Key key)
    {
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.S)]
    [InlineData(Key.F)]
    [InlineData(Key.P)]
    [InlineData(Key.O)]
    [InlineData(Key.W)]
    public void ApplicationControlShortcutsReachWindow(Key key)
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            key, ModifierKeys.Control));
    }

    [Fact]
    public void OrdinaryTypingAndSelectionStayInTextBox()
    {
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            Key.S, ModifierKeys.None));
        Assert.True(EditableTextShortcutPolicy.KeepInTextBox(
            Key.Left, ModifierKeys.Shift));
    }

    [Fact]
    public void AltNavigationReachesWindow()
    {
        Assert.False(EditableTextShortcutPolicy.KeepInTextBox(
            Key.Left, ModifierKeys.Alt));
    }
}
