using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClickRecorder.Models;

namespace ClickRecorder
{
    public partial class App : Application
    {
        // Global exception store – accessible by MainWindow
        public static System.Collections.Generic.List<ExceptionDetail> GlobalExceptions { get; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            EnsureDefaultLightTheme();

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

        private void EnsureDefaultLightTheme()
        {
            const string lightThemePath = "Themes/LightTheme.xaml";

            var mergedDictionaries = Resources.MergedDictionaries;
            for (var index = mergedDictionaries.Count - 1; index >= 0; index--)
            {
                var source = mergedDictionaries[index].Source?.OriginalString;
                if (string.Equals(source, "Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                    mergedDictionaries.RemoveAt(index);
            }

            var hasLightTheme = false;
            foreach (var dictionary in mergedDictionaries)
            {
                var source = dictionary.Source?.OriginalString;
                if (string.Equals(source, lightThemePath, StringComparison.OrdinalIgnoreCase))
                {
                    hasLightTheme = true;
                    break;
                }
            }

            if (!hasLightTheme)
                mergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(lightThemePath, UriKind.Relative) });
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
