using System;
using System.Windows;
using System.Windows.Controls;
using ClickRecorder.Models;
using ClickRecorder.ViewModels;
using ClickRecorder.Views;

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
        private void BtnAttachApp_Click(object sender, RoutedEventArgs e) => _vm.ArmAttachToApplication();
        private void BtnDetachApp_Click(object sender, RoutedEventArgs e) => _vm.ClearAttachedApplication();
        private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await _vm.PlayAsync();
        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => _vm.StopPlay();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _vm.ClearRecording();
        private void BtnAddTextStep_Click(object sender, RoutedEventArgs e) => _vm.AddTextStep();
        private void BtnSaveSequence_Click(object sender, RoutedEventArgs e) => _vm.SaveSequence();
        private void BtnOpenTestCases_Click(object sender, RoutedEventArgs e) => _vm.OpenTestCases(this);
        private void BtnSaveAsTestCase_Click(object sender, RoutedEventArgs e) => _vm.SaveAsTestCase(this);
        private void BtnAddToTestCase_Click(object sender, RoutedEventArgs e) => _vm.AddToTestCase();
        private void BtnOpenJobs_Click(object sender, RoutedEventArgs e) => _vm.OpenJobs(this);
        private void BtnExport_Click(object sender, RoutedEventArgs e) => _vm.ExportReport();

        private void BtnApplyStep_Click(object sender, RoutedEventArgs e) => _vm.ApplyStepDetails();
        private void BtnStepUp_Click(object sender, RoutedEventArgs e)
        {
            var index = _vm.MoveSelectedStep(-1);
            if (index >= 0)
            {
                ClickList.SelectedIndex = index;
            }
        }

        private void BtnStepDown_Click(object sender, RoutedEventArgs e)
        {
            var index = _vm.MoveSelectedStep(1);
            if (index >= 0)
            {
                ClickList.SelectedIndex = index;
            }
        }

        private void ClickList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SelectStep(ClickList.SelectedIndex);
        }
        private void ResultList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
        private void GlobalExList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }
        private void BtnTicket_Click(object sender, RoutedEventArgs e) { }
        private void BtnClearResults_Click(object sender, RoutedEventArgs e) { }

        private void BtnResultDetail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: StepResult result })
            {
                return;
            }

            var detailWindow = new StepResultDetailWindow(result)
            {
                Owner = this
            };
            detailWindow.ShowDialog();
        }

        public void NotifyGlobalException(ExceptionDetail detail) => _vm.NotifyGlobalException(detail);

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
