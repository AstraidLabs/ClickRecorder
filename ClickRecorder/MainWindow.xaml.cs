using System;
using System.Windows;
using System.Windows.Controls;
using ClickRecorder.Models;
using ClickRecorder.Services;
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
            Loaded += (_, _) => ThemeToggle.IsChecked = App.ThemeService.CurrentTheme == AppTheme.Dark;
        }

        private void BtnRecord_Click(object sender, RoutedEventArgs e) => _vm.StartRecord();
        private void BtnStopRecord_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.StopRecord())
            {
                return;
            }

            if (!_vm.HasRecording)
            {
                return;
            }

            var dialog = new SaveRecordingDialog(_vm.SequenceName, _vm.SequenceDescription)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (dialog.ShouldDiscard)
            {
                _vm.ClearRecording();
                return;
            }

            if (dialog.ShouldSave)
            {
                _vm.SaveSequence(dialog.SequenceName, dialog.SequenceDescription);
            }
        }
        private void BtnAttachApp_Click(object sender, RoutedEventArgs e) => _vm.ArmAttachToApplication();
        private void BtnDetachApp_Click(object sender, RoutedEventArgs e) => _vm.ClearAttachedApplication();
        private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await _vm.PlayAsync();
        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => _vm.StopPlay();
        private void BtnLoadPlayback_Click(object sender, RoutedEventArgs e) => _vm.OpenTestCases(this);
        private void BtnClear_Click(object sender, RoutedEventArgs e) => _vm.ClearRecording();
        private void BtnOpenTestCases_Click(object sender, RoutedEventArgs e) => _vm.OpenTestCases(this);
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


        private void BtnGlobalExceptions_Click(object sender, RoutedEventArgs e)
        {
            GlobalExceptionsPopup.IsOpen = !GlobalExceptionsPopup.IsOpen;
        }

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


        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (Application.Current is { } app)
            {
                App.ThemeService.ApplyTheme(app, isDark: true);
            }
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Application.Current is { } app)
            {
                App.ThemeService.ApplyTheme(app, isDark: false);
            }
        }

        public void NotifyGlobalException(ExceptionDetail detail) => _vm.NotifyGlobalException(detail);

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
