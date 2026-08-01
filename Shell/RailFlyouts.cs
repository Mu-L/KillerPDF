using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace KillerPDF
{
    /// <summary>
    /// The rail's three universal buttons (family order, locked 2026-07-30: app-specific
    /// toggles, then ? / language / theme, theme bottom-most) and their flyouts. The theme and
    /// language pickers moved here OUT of the Settings panel - one implementation each, now as
    /// family-standard flyouts: ContextMenus with FlyoutCard/FlyoutGrain chrome, opened against
    /// the content pane's bottom-left corner via FlyoutPlacement so they never cover the rail,
    /// the footer, or the desktop. Their radio/dot sync is SyncPickerState (SettingsPanel.cs),
    /// shared with the Settings panel's remaining pickers.
    /// </summary>
    public partial class MainWindow
    {
        private void RailShortcuts_Click(object sender, RoutedEventArgs e)
        {
            // Same toggle the F1 key and the help footer link use.
            if (ShortcutOverlay.Visibility == Visibility.Visible) FadeOverlayOut(ShortcutOverlay);
            else ShowShortcutsOverlayExclusive();
        }

        private void RailLang_Click(object sender, RoutedEventArgs e) => ToggleRailFlyout(LangFlyout);

        private void RailTheme_Click(object sender, RoutedEventArgs e) => ToggleRailFlyout(ThemeFlyout);

        private void ToggleRailFlyout(ContextMenu menu)
        {
            if (menu.IsOpen) { menu.IsOpen = false; return; }

            // Radios and accent dots reflect live state before the card shows - the same single
            // sync the Settings panel runs on open.
            SyncPickerState();

            // The pane the document sits on bounds the window, the footer and the rail at once -
            // the one corner a flyout can hug without covering any of them.
            if (PagePreviewPanel.Parent is FrameworkElement pane)
                FlyoutPlacement.UsePane(pane);
            FlyoutPlacement.Attach(menu, this);

            menu.IsOpen = true;
            // 150ms ease-out, the family fade (the flyout template replaces the implicit
            // ContextMenu template, whose Loaded trigger normally drives this).
            menu.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
    }
}
