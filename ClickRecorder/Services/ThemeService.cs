using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;

namespace ClickRecorder.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public sealed class ThemeService
    {
        private const string LightThemePath = "Themes/LightTheme.xaml";
        private const string DarkThemePath = "Themes/DarkTheme.xaml";

        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public AppTheme DetectSystemTheme()
        {
            const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

            using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
            var value = key?.GetValue("AppsUseLightTheme");

            return value switch
            {
                int intValue when intValue == 0 => AppTheme.Dark,
                _ => AppTheme.Light
            };
        }

        public void ApplyTheme(Application app, AppTheme theme)
        {
            var mergedDictionaries = app.Resources.MergedDictionaries;
            RemoveThemeDictionaries(mergedDictionaries);

            var source = theme == AppTheme.Dark ? DarkThemePath : LightThemePath;
            mergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });

            CurrentTheme = theme;
        }

        public void ApplyTheme(Application app, bool isDark)
            => ApplyTheme(app, isDark ? AppTheme.Dark : AppTheme.Light);

        public void ApplyTheme(Application app, string themeName)
        {
            var isDark = string.Equals(themeName, "dark", StringComparison.OrdinalIgnoreCase)
                || string.Equals(themeName, "tmave", StringComparison.OrdinalIgnoreCase)
                || string.Equals(themeName, "tmavé", StringComparison.OrdinalIgnoreCase);

            ApplyTheme(app, isDark);
        }

        public void ApplySystemTheme(Application app)
        {
            var systemTheme = DetectSystemTheme();
            ApplyTheme(app, systemTheme);
        }

        public void ToggleTheme(Application app)
        {
            var nextTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            ApplyTheme(app, nextTheme);
        }

        private static void RemoveThemeDictionaries(IList<ResourceDictionary> mergedDictionaries)
        {
            for (var index = mergedDictionaries.Count - 1; index >= 0; index--)
            {
                var source = mergedDictionaries[index].Source?.OriginalString;
                if (string.Equals(source, LightThemePath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source, DarkThemePath, StringComparison.OrdinalIgnoreCase))
                {
                    mergedDictionaries.RemoveAt(index);
                }
            }
        }
    }
}
