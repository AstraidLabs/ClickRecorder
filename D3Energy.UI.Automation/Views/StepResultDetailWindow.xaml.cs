using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using D3Energy.UI.Automation.Models;

namespace D3Energy.UI.Automation.Views;

public partial class StepResultDetailWindow : Window
{
    public StepResultDetailWindow(StepResult result)
    {
        InitializeComponent();

        TxtSummary.Text = result.ToString();

        if (result.Status == StepStatus.Success)
        {
            TxtTitle.Text = "✅ Krok proběhl OK";
            TxtTitle.Foreground = System.Windows.Media.Brushes.LightGreen;
            TxtDetails.Text = "Běh tohoto kroku skončil bez chyby.";
            return;
        }

        TxtTitle.Text = "❌ Krok skončil chybou";
        TxtTitle.Foreground = System.Windows.Media.Brushes.Salmon;
        TxtDetails.Text = result.Exception is null
            ? "Chyba byla zachycena, ale bez detailu výjimky."
            : result.Exception.FullDisplay();

        if (!string.IsNullOrWhiteSpace(result.ScreenshotPath) && File.Exists(result.ScreenshotPath))
        {
            try
            {
                ImgScreenshot.Source = new BitmapImage(new Uri(result.ScreenshotPath));
                ImgScreenshot.Visibility = Visibility.Visible;
                TxtScreenshotLabel.Visibility = Visibility.Visible;
                TxtScreenshotLabel.Text = $"SCREENSHOT: {result.ScreenshotPath}";
            }
            catch
            {
                TxtScreenshotLabel.Visibility = Visibility.Visible;
                TxtScreenshotLabel.Text = $"Screenshot se nepodařilo načíst: {result.ScreenshotPath}";
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
