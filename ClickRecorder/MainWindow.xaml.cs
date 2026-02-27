using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClickRecorder.Models;
using ClickRecorder.Views;
using ClickRecorder.ViewModels;

namespace ClickRecorder
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private void BtnRecord_Click(object sender, RoutedEventArgs e) => _vm.StartRecord();
        private void BtnStopRecord_Click(object sender, RoutedEventArgs e) => _vm.StopRecord();
        private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await _vm.PlayAsync();
        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => _vm.StopPlay();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _vm.ClearRecording();
        private void BtnSaveSequence_Click(object sender, RoutedEventArgs e) => _vm.SaveSequence();
        private void BtnOpenTestCases_Click(object sender, RoutedEventArgs e) => _vm.OpenTestCases(this);
        private void BtnSaveAsTestCase_Click(object sender, RoutedEventArgs e) => _vm.SaveAsTestCase(this);
        private void BtnAddToTestCase_Click(object sender, RoutedEventArgs e) => _vm.AddToTestCase();
        private void BtnOpenJobs_Click(object sender, RoutedEventArgs e) => _vm.OpenJobs(this);
        private void BtnExport_Click(object sender, RoutedEventArgs e) => _vm.ExportReport();

        private void ClickList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var action = _vm.GetRecordedActionAt(ClickList.SelectedIndex);
            if (action is null)
            {
                ClearInspector();
                return;
            }

            ShowInspector(action);
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var step = _vm.GetStepResultAt(ResultList.SelectedIndex);
            if (step is null)
            {
                BtnTicket.IsEnabled = false;
                ClearExPane();
                return;
            }

            ShowEx(step.Exception);
            BtnTicket.IsEnabled = step.Status == StepStatus.Failed;
        }

        private void GlobalExList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowEx(_vm.GetGlobalExceptionAt(GlobalExList.SelectedIndex));
        }

        private void BtnTicket_Click(object sender, RoutedEventArgs e)
        {
            var step = _vm.GetStepResultAt(ResultList.SelectedIndex);
            if (step is null || step.Status != StepStatus.Failed)
            {
                return;
            }

            var dialog = new TicketDialog(step, _vm.GetRecordedActionsSnapshot(), _vm.GetTicketSession())
            {
                Owner = this
            };
            dialog.Show();
        }

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            _vm.ClearResults();
            BtnTicket.IsEnabled = false;
            ClearExPane();
        }

        public void NotifyGlobalException(ExceptionDetail detail)
        {
            _vm.NotifyGlobalException(detail);
        }

        private void ShowInspector(ClickAction action)
        {
            TxtInspHint.Visibility = Visibility.Collapsed;
            InspectorGrid.Visibility = Visibility.Visible;
            InspectorGrid.Children.Clear();
            InspectorGrid.RowDefinitions.Clear();

            var fields = new System.Collections.Generic.List<(string label, string? value, string color)>();
            if (action.Element is { } el)
            {
                fields.Add(("Mode", "⚙ FlaUI Element", "#89B4FA"));
                fields.Add(("Selector", el.Selector, "#CDD6F4"));
                fields.Add(("Type", el.ControlType, "#CDD6F4"));
                fields.Add(("AutomId", el.AutomationId ?? "—", el.AutomationId is null ? "#6C7086" : "#A6E3A1"));
                fields.Add(("Name", el.Name ?? "—", el.Name is null ? "#6C7086" : "#CDD6F4"));
                fields.Add(("Class", el.ClassName ?? "—", "#CDD6F4"));
                fields.Add(("Window", el.WindowTitle ?? "—", "#CDD6F4"));
                fields.Add(("Process", el.ProcessName ?? "—", "#FAB387"));
                if (el.AncestorPath.Count > 0)
                {
                    fields.Add(("Path", string.Join(" › ", el.AncestorPath), "#6C7086"));
                }
            }
            else
            {
                fields.Add(("Mode", "🖱 Souřadnice", "#F9E2AF"));
                fields.Add(("X", action.X.ToString(), "#CDD6F4"));
                fields.Add(("Y", action.Y.ToString(), "#CDD6F4"));
                fields.Add(("Button", action.Button.ToString(), "#CDD6F4"));
            }

            fields.Add(("Delay", $"+{action.DelayAfterPrevious.TotalMilliseconds:F0}ms", "#6C7086"));

            foreach (var (label, value, color) in fields)
            {
                InspectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = InspectorGrid.RowDefinitions.Count - 1;

                var labelBlock = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86)),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 6, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetRow(labelBlock, row);
                Grid.SetColumn(labelBlock, 0);
                InspectorGrid.Children.Add(labelBlock);

                var valueBlock = new TextBlock
                {
                    Text = value ?? string.Empty,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetRow(valueBlock, row);
                Grid.SetColumn(valueBlock, 1);
                InspectorGrid.Children.Add(valueBlock);
            }
        }

        private void ClearInspector()
        {
            TxtInspHint.Visibility = Visibility.Visible;
            InspectorGrid.Visibility = Visibility.Collapsed;
            InspectorGrid.Children.Clear();
            InspectorGrid.RowDefinitions.Clear();
        }

        private void ShowEx(ExceptionDetail? ex)
        {
            if (ex is null)
            {
                ClearExPane();
                return;
            }

            ExBox.Visibility = Visibility.Visible;
            TxtExType.Text = ex.Type;
            TxtExMessage.Text = ex.Message;
            TxtExSource.Text = string.IsNullOrEmpty(ex.Source) ? string.Empty : $"Source: {ex.Source}";
            TxtExTime.Text = $"Zachyceno: {ex.CapturedAt:HH:mm:ss.fff}";

            if (ex.InnerException is not null)
            {
                TxtExInner.Visibility = Visibility.Visible;
                TxtExInner.Text = $"Inner: {ex.InnerException.ShortType}: {ex.InnerException.Message}";
            }
            else
            {
                TxtExInner.Visibility = Visibility.Collapsed;
            }

            TxtStack.Text = ex.FullDisplay();
        }

        private void ClearExPane()
        {
            ExBox.Visibility = Visibility.Collapsed;
            TxtStack.Text = "← Vyber krok ve výsledcích pro zobrazení call stacku";
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
