using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using ClickRecorder.Models;
using ClickRecorder.Services;

namespace ClickRecorder.Views
{
    public partial class TestCaseEditorWindow : Window
    {
        private readonly TestCaseService _svc;
        private          TestCase        _tc;
        private          int?            _dbId;
        private          bool            _dirty;
        private          bool            _suppress;

        // Currently selected indices
        private int _secIdx  = -1;
        private int _stepIdx = -1;

        // Clicks waiting to be pasted from the recorder
        private List<ClickAction> _pendingClicks = new();

        public event EventHandler? Saved;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters             = { new JsonStringEnumConverter() }
        };

        // ── Constructors ──────────────────────────────────────────────────────

        public TestCaseEditorWindow(TestCaseService svc)
            : this(svc, new TestCase(), null) { }

        public TestCaseEditorWindow(TestCaseService svc, List<ClickAction> clicks,
                                    string title, string sectionName = "Kroky")
            : this(svc, TestCase.FromClickActions(title, clicks), null) { }

        public TestCaseEditorWindow(TestCaseService svc, TestCase tc, int? dbId)
        {
            InitializeComponent();
            _svc  = svc;
            _tc   = tc;
            _dbId = dbId;
            Bind();
        }

        // ── Accept clicks from main recorder (called from outside) ────────────

        public void SetPendingClicks(List<ClickAction> clicks, string recordingName)
        {
            _pendingClicks = clicks;
            TxtRecorderInfo.Text = clicks.Count > 0
                ? $"📥 {clicks.Count} kliků z nahrávky „{recordingName}“"
                : "Žádná kliknutí z recorderu";
            BtnPasteClicks.IsEnabled = clicks.Count > 0 && _secIdx >= 0;
        }

        // ── Public API: called from MainWindow ──────────────────────────────────

        /// <summary>
        /// Adds a new section filled with <paramref name="clicks"/> and selects it.
        /// Called from the main window "➕ Přidat do Test Case" button.
        /// </summary>
        public void AddSection(string sectionName, List<ClickAction> clicks)
        {
            var sec = TestCaseSection.FromClickActions(
                sectionName, clicks, _tc.Sections.Count + 1);
            _tc.Sections.Add(sec);

            int newIdx = _tc.Sections.Count - 1;
            RefreshSections(newIdx);
            SetDirty();

            TxtFooter.Text =
                $"✓ Přidána sekce „{sectionName}“ s {sec.Steps.Count} kroky — ulož pro uložení do DB";
        }

        // ── Bind model → UI ───────────────────────────────────────────────────

        private void Bind()
        {
            _suppress = true;
            TxtTitle.Text           = _tc.Title;
            TxtDescription.Text     = _tc.Description;
            TxtComponent.Text       = _tc.Component;
            TxtTags.Text            = string.Join(", ", _tc.Tags);
            TxtVersion.Text         = _tc.Version;
            TxtPreconditions.Text   = _tc.Preconditions;
            TxtExpectedOutcome.Text = _tc.ExpectedOutcome;
            TxtNotes.Text           = _tc.Notes;
            CboPriority.SelectedIndex = (int)_tc.Priority;
            CboStatus.SelectedIndex   = (int)_tc.Status;
            TxtWindowTitle.Text = _dbId.HasValue ? $"🧪  {_tc.Title}" : "🧪  Nový Test Case";
            TxtWindowSub.Text   = $"{_tc.Sections.Count} sekcí · {_tc.TotalSteps} kroků · {_tc.TotalClicks} kliků";
            _suppress = false;

            RefreshSections();
            RefreshJson();
            _dirty = false;
            DirtyBadge.Visibility = Visibility.Collapsed;
        }

        // ── Flush UI → model ─────────────────────────────────────────────────

        private void FlushMeta()
        {
            _tc.Title           = TxtTitle.Text.Trim();
            _tc.Description     = TxtDescription.Text.Trim();
            _tc.Component       = TxtComponent.Text.Trim();
            _tc.Version         = TxtVersion.Text.Trim();
            _tc.Preconditions   = TxtPreconditions.Text.Trim();
            _tc.ExpectedOutcome = TxtExpectedOutcome.Text.Trim();
            _tc.Notes           = TxtNotes.Text.Trim();
            _tc.Tags = TxtTags.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            _tc.Priority = (TestCasePriority)CboPriority.SelectedIndex;
            _tc.Status   = (TestCaseStatus)  CboStatus.SelectedIndex;
        }

        // ── Sections ──────────────────────────────────────────────────────────

        private void RefreshSections(int selectIdx = -1)
        {
            SectionList.Items.Clear();
            for (int i = 0; i < _tc.Sections.Count; i++)
            {
                var s = _tc.Sections[i];
                SectionList.Items.Add(
                    $"  {i + 1:D2}.  {s.Name,-22}  [{s.Steps.Count} kroků]");
            }
            if (selectIdx >= 0 && selectIdx < _tc.Sections.Count)
                SectionList.SelectedIndex = selectIdx;
            else if (_secIdx >= 0 && _secIdx < _tc.Sections.Count)
                SectionList.SelectedIndex = _secIdx;

            TxtWindowSub.Text =
                $"{_tc.Sections.Count} sekcí · {_tc.TotalSteps} kroků · {_tc.TotalClicks} kliků";
        }

        private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _secIdx = SectionList.SelectedIndex;
            bool sel = _secIdx >= 0 && _secIdx < _tc.Sections.Count;
            BtnDeleteSec.IsEnabled   = sel;
            BtnPasteClicks.IsEnabled = sel && _pendingClicks.Count > 0;

            if (!sel)
            {
                TxtSectionHeader.Text = "KROKY  ← vyber sekci vlevo";
                StepList.Items.Clear();
                TxtStepCount.Text = "";
                return;
            }

            var sec = _tc.Sections[_secIdx];
            TxtSectionHeader.Text = $"KROKY  –  {sec.Name.ToUpper()}";
            RefreshSteps();
        }

        private void BtnAddSection_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewSectionName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Sekce {_tc.Sections.Count + 1}";

            _tc.Sections.Add(new TestCaseSection
            {
                Order = _tc.Sections.Count + 1,
                Name  = name
            });
            TxtNewSectionName.Clear();
            RefreshSections(_tc.Sections.Count - 1);
            SetDirty();
        }

        private void BtnDeleteSec_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _secIdx >= _tc.Sections.Count) return;
            string n = _tc.Sections[_secIdx].Name;
            if (MessageBox.Show($"Smazat sekci „{n}“ i se všemi kroky?", "Potvrdit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _tc.Sections.RemoveAt(_secIdx);
            RenumberSections();
            _secIdx = -1;
            RefreshSections();
            SetDirty();
        }

        private void BtnSecUp_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx <= 0) return;
            (_tc.Sections[_secIdx], _tc.Sections[_secIdx - 1]) =
            (_tc.Sections[_secIdx - 1], _tc.Sections[_secIdx]);
            RenumberSections();
            RefreshSections(_secIdx - 1);
            SetDirty();
        }

        private void BtnSecDown_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _secIdx >= _tc.Sections.Count - 1) return;
            (_tc.Sections[_secIdx], _tc.Sections[_secIdx + 1]) =
            (_tc.Sections[_secIdx + 1], _tc.Sections[_secIdx]);
            RenumberSections();
            RefreshSections(_secIdx + 1);
            SetDirty();
        }

        private void RenumberSections()
        {
            for (int i = 0; i < _tc.Sections.Count; i++) _tc.Sections[i].Order = i + 1;
        }

        // ── Paste pending clicks into selected section ────────────────────────

        private void BtnPasteClicks_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _pendingClicks.Count == 0) return;
            var sec     = _tc.Sections[_secIdx];
            int startOrd = sec.Steps.Count + 1;

            var added = TestCaseSection.FromClickActions("_tmp", _pendingClicks);
            foreach (var step in added.Steps)
            {
                step.Order = startOrd++;
                sec.Steps.Add(step);
            }

            RefreshSteps();
            RefreshSections(_secIdx);
            SetDirty();
            TxtFooter.Text =
                $"✓ Přidáno {added.Steps.Count} kroků do sekce „{sec.Name}“";

            // Clear pending to avoid double-paste
            _pendingClicks = new();
            BtnPasteClicks.IsEnabled = false;
            TxtRecorderInfo.Text = "Kliknutí vložena ✓";
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        private void RefreshSteps(int selectIdx = -1)
        {
            if (_secIdx < 0 || _secIdx >= _tc.Sections.Count) return;
            var sec = _tc.Sections[_secIdx];

            StepList.Items.Clear();
            foreach (var s in sec.Steps)
            {
                string click = s.Click is not null
                    ? (s.Click.Element is not null
                       ? $"  ⚙ {s.Click.Element.Selector}"
                       : $"  🖱 ({s.Click.X},{s.Click.Y})")
                    : "  📝";
                string exp = string.IsNullOrEmpty(s.ExpectedResult) ? "" : $"  → {s.ExpectedResult}";
                StepList.Items.Add($"  {s.Order:D2}.  {s.Description}{click}{exp}");
            }
            TxtStepCount.Text = $"{sec.Steps.Count} kroků";

            if (selectIdx >= 0 && selectIdx < sec.Steps.Count)
                StepList.SelectedIndex = selectIdx;
        }

        private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _stepIdx = StepList.SelectedIndex;
            BtnDeleteStep.IsEnabled = _stepIdx >= 0;

            if (_secIdx < 0 || _stepIdx < 0 ||
                _stepIdx >= _tc.Sections[_secIdx].Steps.Count) return;

            _suppress = true;
            var step = _tc.Sections[_secIdx].Steps[_stepIdx];
            TxtStepDesc.Text     = step.Description;
            TxtStepExpected.Text = step.ExpectedResult;
            _suppress = false;
        }

        private void BtnAddStep_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _secIdx >= _tc.Sections.Count)
            { TxtFooter.Text = "⚠ Nejdřív vyber sekci"; return; }
            var sec = _tc.Sections[_secIdx];
            sec.Steps.Add(new TestCaseStep
            {
                Order       = sec.Steps.Count + 1,
                Description = "Nový krok"
            });
            RefreshSteps(sec.Steps.Count - 1);
            SetDirty();
        }

        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _stepIdx < 0) return;
            _tc.Sections[_secIdx].Steps.RemoveAt(_stepIdx);
            RenumberSteps(_secIdx);
            RefreshSteps();
            RefreshSections(_secIdx);
            SetDirty();
        }

        private void BtnStepUp_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _stepIdx <= 0) return;
            var steps = _tc.Sections[_secIdx].Steps;
            (steps[_stepIdx], steps[_stepIdx - 1]) = (steps[_stepIdx - 1], steps[_stepIdx]);
            RenumberSteps(_secIdx);
            RefreshSteps(_stepIdx - 1);
            SetDirty();
        }

        private void BtnStepDown_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _stepIdx < 0) return;
            var steps = _tc.Sections[_secIdx].Steps;
            if (_stepIdx >= steps.Count - 1) return;
            (steps[_stepIdx], steps[_stepIdx + 1]) = (steps[_stepIdx + 1], steps[_stepIdx]);
            RenumberSteps(_secIdx);
            RefreshSteps(_stepIdx + 1);
            SetDirty();
        }

        private void BtnSaveStep_Click(object sender, RoutedEventArgs e)
        {
            if (_secIdx < 0 || _stepIdx < 0) return;
            var step = _tc.Sections[_secIdx].Steps[_stepIdx];
            step.Description  = TxtStepDesc.Text.Trim();
            step.ExpectedResult = TxtStepExpected.Text.Trim();
            RefreshSteps(_stepIdx);
            SetDirty();
            TxtFooter.Text = "✓ Krok uložen";
        }

        private void RenumberSteps(int secIdx)
        {
            var steps = _tc.Sections[secIdx].Steps;
            for (int i = 0; i < steps.Count; i++) steps[i].Order = i + 1;
        }

        // ── JSON ─────────────────────────────────────────────────────────────

        private void RefreshJson()
        {
            FlushMeta();
            try { TxtJson.Text = JsonSerializer.Serialize(_tc, _jsonOpts); }
            catch (Exception ex) { TxtJson.Text = $"// {ex.Message}"; }
        }

        private void BtnRefreshJson_Click(object sender, RoutedEventArgs e) => RefreshJson();

        // ── Save / Export ─────────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTitle.Text))
            { TxtFooter.Text = "❌ Zadej název test case"; return; }

            FlushMeta();
            _tc.UpdatedAt = DateTime.UtcNow;

            try
            {
                if (_dbId.HasValue) _svc.SaveById(_dbId.Value, _tc);
                else { var ent = _svc.Save(_tc); _dbId = ent.Id; }

                _dirty                = false;
                DirtyBadge.Visibility = Visibility.Collapsed;
                TxtWindowTitle.Text   = $"🧪  {_tc.Title}";
                TxtFooter.Text        = $"✓ Uloženo [{DateTime.Now:HH:mm:ss}]  –  {_tc.Sections.Count} sekcí / {_tc.TotalSteps} kroků";
                RefreshJson();
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { TxtFooter.Text = $"❌ {ex.Message}"; }
        }

        private void BtnExportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_dbId is null) BtnSave_Click(sender, e);
            if (_dbId is null) return;
            try
            {
                string path = _svc.ExportToFile(_dbId.Value);
                TxtFooter.Text = $"📤 {path}";
                Process.Start(new ProcessStartInfo(
                    System.IO.Path.GetDirectoryName(path)!) { UseShellExecute = true });
            }
            catch (Exception ex) { TxtFooter.Text = $"❌ {ex.Message}"; }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty && MessageBox.Show("Neuložené změny. Zavřít?", "Zavřít",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            Close();
        }

        // ── Dirty ─────────────────────────────────────────────────────────────

        private void MarkDirty(object sender, TextChangedEventArgs e)      => SetDirty();
        private void MarkDirtyCombo(object sender, SelectionChangedEventArgs e) => SetDirty();

        private void SetDirty()
        {
            if (_suppress) return;
            _dirty = true;
            DirtyBadge.Visibility = Visibility.Visible;
        }
    }
}
