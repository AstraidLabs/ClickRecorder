using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClickRecorder.Data.Entities;
using ClickRecorder.Models;
using ClickRecorder.Services;
using Microsoft.Win32;

namespace ClickRecorder.Views
{
    public partial class TestCasesWindow : Window
    {
        private readonly TestCaseService     _svc;
        private List<DbTestCase>             _all  = new();
        private List<DbTestCase>             _view = new();

        // Fired when user wants to load a TC's clicks into the recorder
        public event EventHandler<List<ClickAction>>? LoadToRecorderRequested;

        public TestCasesWindow(TestCaseService svc)
        {
            InitializeComponent();
            _svc = svc;
            Refresh();
        }

        // ── Data ─────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _all = _svc.GetAll();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string q = TxtFilter.Text.Trim().ToLower();
            _view = string.IsNullOrEmpty(q)
                ? new List<DbTestCase>(_all)
                : _all.Where(x =>
                    x.Title.ToLower().Contains(q) ||
                    x.Component.ToLower().Contains(q) ||
                    x.Tags.ToLower().Contains(q) ||
                    x.Author.ToLower().Contains(q)).ToList();

            TcList.Items.Clear();
            foreach (var e in _view)
            {
                string prio = (TestCasePriority)e.Priority switch
                {
                    TestCasePriority.Critical => "🔴",
                    TestCasePriority.High     => "🟠",
                    TestCasePriority.Medium   => "🟡",
                    _                         => "🟢"
                };
                string status = (TestCaseStatus)e.Status switch
                {
                    TestCaseStatus.Active     => "✅",
                    TestCaseStatus.Draft      => "📝",
                    TestCaseStatus.Deprecated => "🚫",
                    _                         => "?"
                };
                string tags = string.IsNullOrEmpty(e.Tags) ? "" : $"  [{e.Tags}]";
                TcList.Items.Add($"{prio} {status}  {e.Title,-40}  {e.Component,-15}{tags}");
            }

            TxtCount.Text = $"{_view.Count}/{_all.Count}";
        }

        private void TxtFilter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

        // ── Selection & Detail ────────────────────────────────────────────────

        private void TcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool sel = TcList.SelectedIndex >= 0;
            BtnEdit.IsEnabled   = sel;
            BtnDelete.IsEnabled = sel;

            if (!sel) { TxtDetail.Text = "← Vyber test case"; return; }

            int idx = TcList.SelectedIndex;
            if (idx >= _view.Count) return;
            var entity = _view[idx];
            var tc = _svc.Load(entity.Id);
            if (tc is null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"ID:         {tc.Id}");
            sb.AppendLine($"Verze:      {tc.Version}");
            sb.AppendLine($"Priorita:   {tc.PriorityIcon} {tc.Priority}");
            sb.AppendLine($"Status:     {tc.StatusIcon} {tc.Status}");
            sb.AppendLine($"Komponenta: {tc.Component}");
            sb.AppendLine($"Tagy:       {string.Join(", ", tc.Tags)}");
            sb.AppendLine($"Autor:      {tc.Author}");
            sb.AppendLine($"Vytvořeno:  {tc.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
            sb.AppendLine($"Upraveno:   {tc.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(tc.Description))
            { sb.AppendLine("Popis:"); sb.AppendLine(tc.Description); sb.AppendLine(); }

            if (!string.IsNullOrEmpty(tc.Preconditions))
            { sb.AppendLine("Preconditions:"); sb.AppendLine(tc.Preconditions); sb.AppendLine(); }

            if (tc.Steps.Count > 0)
            {
                sb.AppendLine($"Kroky ({tc.Steps.Count}):");
                foreach (var s in tc.Steps)
                {
                    sb.AppendLine($"  {s.Order:D2}. {s.Description}");
                    if (!string.IsNullOrEmpty(s.ExpectedResult))
                        sb.AppendLine($"      → {s.ExpectedResult}");
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(tc.ExpectedOutcome))
            { sb.AppendLine("Očekávaný výsledek:"); sb.AppendLine(tc.ExpectedOutcome); }

            TxtDetail.Text = sb.ToString();
        }

        // ── CRUD actions ──────────────────────────────────────────────────────

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            var editor = new TestCaseEditorWindow(_svc) { Owner = this };
            editor.Saved += (_, _) => Refresh();
            editor.Show();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e) => OpenEditor();

        private void TcList_DoubleClick(object sender, MouseButtonEventArgs e) => OpenEditor();

        private void OpenEditor()
        {
            int idx = TcList.SelectedIndex;
            if (idx < 0 || idx >= _view.Count) return;
            var entity = _view[idx];
            var tc = _svc.Load(entity.Id);
            if (tc is null) return;

            var editor = new TestCaseEditorWindow(_svc, tc, entity.Id) { Owner = this };
            editor.Saved += (_, _) => Refresh();
            editor.Show();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            int idx = TcList.SelectedIndex;
            if (idx < 0 || idx >= _view.Count) return;
            var entity = _view[idx];

            if (MessageBox.Show($"Smazat test case '{entity.Title}'?", "Potvrdit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _svc.Delete(entity.Id);
            Refresh();
            TxtFooter.Text = $"🗑 Smazáno: {entity.Title}";
        }

        // ── Export / Import ───────────────────────────────────────────────────

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            int idx = TcList.SelectedIndex;
            if (idx < 0 || idx >= _view.Count) { TxtFooter.Text = "❌ Vyber test case"; return; }
            try
            {
                string path = _svc.ExportToFile(_view[idx].Id);
                TxtFooter.Text = $"📤 Exportováno: {path}";
                Process.Start(new ProcessStartInfo(System.IO.Path.GetDirectoryName(path)!) { UseShellExecute = true });
            }
            catch (Exception ex) { TxtFooter.Text = $"❌ {ex.Message}"; }
        }

        private void BtnExportAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _svc.ExportAllToFile();
                TxtFooter.Text = $"📤 Exportováno vše: {path}";
                Process.Start(new ProcessStartInfo(System.IO.Path.GetDirectoryName(path)!) { UseShellExecute = true });
            }
            catch (Exception ex) { TxtFooter.Text = $"❌ {ex.Message}"; }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Import Test Case JSON",
                Filter = "JSON soubory (*.json)|*.json|Všechny soubory (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var (imported, skipped) = _svc.ImportFromFile(dlg.FileName);
                Refresh();
                TxtFooter.Text = $"📥 Importováno {imported}, přeskočeno {skipped}";
            }
            catch (Exception ex) { TxtFooter.Text = $"❌ Import selhal: {ex.Message}"; }
        }
    }
}
