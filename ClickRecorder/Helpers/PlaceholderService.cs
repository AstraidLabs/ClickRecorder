using System.Windows;
using System.Windows.Controls;

namespace ClickRecorder.Helpers
{
    public static class PlaceholderService
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(PlaceholderService),
                new FrameworkPropertyMetadata(string.Empty));

        public static void SetText(DependencyObject element, string value) =>
            element.SetValue(TextProperty, value);

        public static string GetText(DependencyObject element) =>
            (string)element.GetValue(TextProperty);

        public static readonly DependencyProperty IsMonitoringProperty =
            DependencyProperty.RegisterAttached(
                "IsMonitoring",
                typeof(bool),
                typeof(PlaceholderService),
                new PropertyMetadata(false, OnIsMonitoringChanged));

        public static void SetIsMonitoring(DependencyObject element, bool value) =>
            element.SetValue(IsMonitoringProperty, value);

        public static bool GetIsMonitoring(DependencyObject element) =>
            (bool)element.GetValue(IsMonitoringProperty);

        private static void OnIsMonitoringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            if ((bool)e.NewValue)
            {
                tb.TextChanged += (_, __) => tb.InvalidateVisual();
                tb.GotKeyboardFocus += (_, __) => tb.InvalidateVisual();
                tb.LostKeyboardFocus += (_, __) => tb.InvalidateVisual();
                tb.Loaded += (_, __) => tb.InvalidateVisual();
            }
        }
    }
}