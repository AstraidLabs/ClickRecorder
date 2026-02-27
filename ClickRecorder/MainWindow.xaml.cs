using System;
using System.Threading.Tasks;
using System.Windows;
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

        private void ClickList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
        private void ResultList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
        private void GlobalExList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
        private void BtnTicket_Click(object sender, RoutedEventArgs e) { }
        private void BtnClearResults_Click(object sender, RoutedEventArgs e) { }

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
