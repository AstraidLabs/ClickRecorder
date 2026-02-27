using System.Collections.Generic;
using System.Windows;
using ClickRecorder.Models;
using ClickRecorder.Services;

namespace ClickRecorder.Views
{
    public partial class TicketDialog : Window
    {
        private readonly StepResult       _failedStep;
        private readonly List<ClickAction> _allSteps;
        private readonly TestSession       _session;

        public TicketDialog(StepResult failedStep, List<ClickAction> allSteps, TestSession session)
        {
            InitializeComponent();
            _failedStep = failedStep;
            _allSteps   = allSteps;
            _session    = session;
            Refresh();
        }

        private void Refresh()
        {
            TxtTicket.Text = TicketGenerator.Generate(
                _failedStep, _allSteps, _session,
                TxtApp.Text.Trim(), TxtVersion.Text.Trim());
        }

        private void OnFieldChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => Refresh();

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtTicket.Text);
            BtnCopy.Content = "✓ Zkopírováno!";
            var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = System.TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { BtnCopy.Content = "📋 Kopírovat do schránky"; timer.Stop(); };
            timer.Start();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
