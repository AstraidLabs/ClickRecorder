using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using D3Energy.UI.Automation.Models;
using D3Energy.UI.Automation.Services;

namespace D3Energy.UI.Automation
{
    public partial class App : Application
    {
        // Global exception store – accessible by MainWindow
        public static System.Collections.Generic.List<ExceptionDetail> GlobalExceptions { get; } = new();

        public static ThemeService ThemeService { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ThemeService.ApplySystemTheme(this);

            // 1) WPF UI thread unhandled exceptions
            DispatcherUnhandledException += (_, args) =>
            {
                CaptureGlobal(args.Exception, "DispatcherUnhandledException");
                args.Handled = true; // keep app running – just log it
            };

            // 2) Non-UI thread unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    CaptureGlobal(ex, "AppDomain.UnhandledException");
            };

            // 3) Unobserved Task exceptions
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                CaptureGlobal(args.Exception, "TaskScheduler.UnobservedTaskException");
                args.SetObserved();
            };
        }

        private static void CaptureGlobal(Exception ex, string source)
        {
            var detail = ExceptionDetail.FromException(ex);
            detail.Source = source;
            GlobalExceptions.Add(detail);

            // Notify MainWindow if open
            Current?.Dispatcher.InvokeAsync(() =>
            {
                if (Current?.MainWindow is MainWindow mw)
                    mw.NotifyGlobalException(detail);
            });
        }
    }
}
