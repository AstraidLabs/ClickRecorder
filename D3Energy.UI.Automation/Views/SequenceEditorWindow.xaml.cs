using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using D3Energy.UI.Automation.Data.Entities;
using D3Energy.UI.Automation.Models;
using D3Energy.UI.Automation.Services;
using Microsoft.Win32;

namespace D3Energy.UI.Automation.Views
{
    public partial class SequenceEditorWindow : Window
    {
        private readonly DatabaseService _db;
        private List<DbSequence> _sequences = new();
        private List<ClickAction> _steps = new();
        private int _seqIdx = -1;
        private int _stepIdx = -1;
        private bool _suppress;
        private bool _dirty;

        public event EventHandler<List<ClickAction>>? SequenceLoadRequested;

        public SequenceEditorWindow(DatabaseService db)
        {
            InitializeComponent();
            _db = db;
            RefreshSequences();
        }

        private void RefreshSequences(int selectSequenceId = 0)
        {
            _sequences = _db.GetAllSequences();
            SequenceList.Items.Clear();
            foreach (var seq in _sequences)
            {
                int count = ParseSteps(seq.StepsJson).Count;
                SequenceList.Items.Add($"📂 {seq.Name,-24} [{count,3} kroků] {seq.UpdatedAt.ToLocalTime():dd.MM.yy HH:mm}");
            }

            CboMoveTarget.ItemsSource = _sequences;

            if (_sequences.Count == 0)
            {
                _seqIdx = -1;
                _steps = new();
                RefreshSteps();
                BindSequenceMeta();
                return;
            }

            if (selectSequenceId > 0)
            {
                int idx = _sequences.FindIndex(s => s.Id == selectSequenceId);
                if (idx >= 0) SequenceList.SelectedIndex = idx;
            }
            else if (_seqIdx >= 0 && _seqIdx < _sequences.Count)
            {
                SequenceList.SelectedIndex = _seqIdx;
            }
            else
            {
                SequenceList.SelectedIndex = 0;
            }
        }

        private static List<ClickAction> ParseSteps(string json)
        {
            try { return JsonSerializer.Deserialize<List<ClickAction>>(json) ?? new(); }
            catch { return new(); }
        }

        private void SequenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _seqIdx = SequenceList.SelectedIndex;
            bool sel = _seqIdx >= 0 && _seqIdx < _sequences.Count;
            BtnDeleteSequence.IsEnabled = sel;
            BtnLoadToRecorder.IsEnabled = sel;

            if (!sel)
            {
                _steps = new();
                _stepIdx = -1;
                RefreshSteps();
                BindSequenceMeta();
                return;
            }

            var seq = _sequences[_seqIdx];
            _steps = ParseSteps(seq.StepsJson);
            RenumberSteps();
            RefreshSteps(0);
            BindSequenceMeta();
            SetDirty(false);
        }

        private void BindSequenceMeta()
        {
            _suppress = true;
            if (_seqIdx < 0 || _seqIdx >= _sequences.Count)
            {
                TxtSequenceName.Text = string.Empty;
                TxtSequenceDescription.Text = string.Empty;
                TxtStepsHeader.Text = "KROKY";
                TxtStepsCount.Text = string.Empty;
                BindStepMeta();
                _suppress = false;
                return;
            }

            var seq = _sequences[_seqIdx];
            TxtSequenceName.Text = seq.Name;
            TxtSequenceDescription.Text = seq.Description;
            TxtStepsHeader.Text = $"KROKY – {seq.Name.ToUpper()}";
            TxtStepsCount.Text = $"{_steps.Count} kroků";
            BindStepMeta();
            _suppress = false;
        }

        private void RefreshSteps(int selectIdx = -1)
        {
            StepList.Items.Clear();
            foreach (var step in _steps)
            {
                string kind = step.Kind == ActionKind.TypeText
                    ? $"TEXT '{step.TextToType ?? string.Empty}'"
                    : $"{step.Button} @ ({step.X},{step.Y})";
                StepList.Items.Add($"{step.Id:D2}. {step.DisplayStepTitle,-20} {kind,-24} +{step.DelayAfterPrevious.TotalMilliseconds:F0}ms");
            }

            if (selectIdx >= 0 && selectIdx < _steps.Count)
            {
                StepList.SelectedIndex = selectIdx;
            }

            TxtStepsCount.Text = $"{_steps.Count} kroků";
            BtnDeleteStep.IsEnabled = StepList.SelectedIndex >= 0;
            BtnMoveStep.IsEnabled = StepList.SelectedIndex >= 0;
        }

        private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _stepIdx = StepList.SelectedIndex;
            BtnDeleteStep.IsEnabled = _stepIdx >= 0;
            BtnMoveStep.IsEnabled = _stepIdx >= 0;
            BtnSaveStep.IsEnabled = _stepIdx >= 0;
            BindStepMeta();
        }

        private void BindStepMeta()
        {
            _suppress = true;
            if (_stepIdx < 0 || _stepIdx >= _steps.Count)
            {
                TxtStepName.Text = string.Empty;
                TxtStepDescription.Text = string.Empty;
                CboActionKind.SelectedIndex = 0;
                CboButton.SelectedIndex = 0;
                TxtTextToType.Text = string.Empty;
                TxtDelayMs.Text = "0";
                CboMoveTarget.SelectedItem = null;
                _suppress = false;
                return;
            }

            var step = _steps[_stepIdx];
            TxtStepName.Text = step.StepName;
            TxtStepDescription.Text = step.StepDescription;
            CboActionKind.SelectedIndex = step.Kind == ActionKind.Click ? 0 : 1;
            CboButton.SelectedIndex = (int)step.Button;
            TxtTextToType.Text = step.TextToType ?? string.Empty;
            TxtDelayMs.Text = ((int)step.DelayAfterPrevious.TotalMilliseconds).ToString();
            CboMoveTarget.SelectedItem = _sequences.FirstOrDefault(s => _seqIdx >= 0 && s.Id != _sequences[_seqIdx].Id);
            _suppress = false;
        }

        private void BtnNewSequence_Click(object sender, RoutedEventArgs e)
        {
            var seq = _db.SaveSequence($"Nová sekvence {DateTime.Now:dd.MM HH:mm}", "", new());
            RefreshSequences(seq.Id);
            TxtFooter.Text = $"✓ Vytvořena sekvence '{seq.Name}'";
        }

        private void BtnDeleteSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0 || _seqIdx >= _sequences.Count) return;
            var seq = _sequences[_seqIdx];
            if (MessageBox.Show($"Smazat sekvenci '{seq.Name}'?", "Potvrdit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _db.DeleteSequence(seq.Id);
            RefreshSequences();
            TxtFooter.Text = $"🗑 Sekvence '{seq.Name}' smazána";
        }

        private void BtnLoadToRecorder_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0 || _seqIdx >= _sequences.Count) return;
            SequenceLoadRequested?.Invoke(this, new List<ClickAction>(_steps));
            TxtFooter.Text = $"📥 Sekvence '{_sequences[_seqIdx].Name}' načtena do recorderu";
        }

        private void BtnAddStep_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0) return;
            _steps.Add(new ClickAction
            {
                Id = _steps.Count + 1,
                Kind = ActionKind.Click,
                Button = ClickButton.Left,
                StepName = $"Krok {_steps.Count + 1}",
                DelayAfterPrevious = TimeSpan.Zero,
                RecordedAt = DateTime.UtcNow
            });
            RefreshSteps(_steps.Count - 1);
            SetDirty();
        }

        private void BtnDeleteStep_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIdx < 0 || _stepIdx >= _steps.Count) return;
            _steps.RemoveAt(_stepIdx);
            RenumberSteps();
            RefreshSteps();
            BindStepMeta();
            SetDirty();
        }

        private void BtnStepUp_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIdx <= 0 || _stepIdx >= _steps.Count) return;
            (_steps[_stepIdx], _steps[_stepIdx - 1]) = (_steps[_stepIdx - 1], _steps[_stepIdx]);
            RenumberSteps();
            RefreshSteps(_stepIdx - 1);
            SetDirty();
        }

        private void BtnStepDown_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIdx < 0 || _stepIdx >= _steps.Count - 1) return;
            (_steps[_stepIdx], _steps[_stepIdx + 1]) = (_steps[_stepIdx + 1], _steps[_stepIdx]);
            RenumberSteps();
            RefreshSteps(_stepIdx + 1);
            SetDirty();
        }

        private void BtnSaveStep_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIdx < 0 || _stepIdx >= _steps.Count) return;
            if (!TryApplyStepEditor(_steps[_stepIdx])) return;
            RefreshSteps(_stepIdx);
            SetDirty();
            TxtFooter.Text = "✓ Krok uložen";
        }

        private bool TryApplyStepEditor(ClickAction step)
        {
            if (!int.TryParse(TxtDelayMs.Text.Trim(), out int delayMs) || delayMs < 0)
            {
                TxtFooter.Text = "❌ Čekání musí být nezáporné číslo (ms).";
                return false;
            }

            step.StepName = TxtStepName.Text.Trim();
            step.StepDescription = TxtStepDescription.Text.Trim();
            step.Kind = CboActionKind.SelectedIndex == 1 ? ActionKind.TypeText : ActionKind.Click;
            step.Button = (ClickButton)Math.Max(0, CboButton.SelectedIndex);
            step.TextToType = string.IsNullOrWhiteSpace(TxtTextToType.Text) ? null : TxtTextToType.Text;
            step.DelayAfterPrevious = TimeSpan.FromMilliseconds(delayMs);
            return true;
        }

        private void BtnMoveStep_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0 || _stepIdx < 0 || _stepIdx >= _steps.Count) return;
            if (CboMoveTarget.SelectedItem is not DbSequence target) return;
            var src = _sequences[_seqIdx];
            if (target.Id == src.Id) return;

            var targetSteps = ParseSteps(target.StepsJson);
            var moved = _steps[_stepIdx];
            _steps.RemoveAt(_stepIdx);
            RenumberSteps();

            moved.Id = targetSteps.Count + 1;
            targetSteps.Add(moved);

            _db.SaveSequenceById(src.Id, TxtSequenceName.Text.Trim(), TxtSequenceDescription.Text.Trim(), _steps);
            _db.SaveSequenceById(target.Id, target.Name, target.Description, targetSteps);

            RefreshSequences(src.Id);
            TxtFooter.Text = $"↪ Krok přesunut do sekvence '{target.Name}'";
        }

        private void BtnSaveSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0 || _seqIdx >= _sequences.Count) return;
            var seq = _sequences[_seqIdx];

            if (string.IsNullOrWhiteSpace(TxtSequenceName.Text))
            {
                TxtFooter.Text = "❌ Název sekvence je povinný.";
                return;
            }

            if (_stepIdx >= 0 && _stepIdx < _steps.Count && !TryApplyStepEditor(_steps[_stepIdx])) return;

            RenumberSteps();
            _db.SaveSequenceById(seq.Id, TxtSequenceName.Text.Trim(), TxtSequenceDescription.Text.Trim(), _steps);
            RefreshSequences(seq.Id);
            SetDirty(false);
            TxtFooter.Text = $"✓ Sekvence '{TxtSequenceName.Text.Trim()}' uložena";
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json|Všechny soubory (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var import = JsonSerializer.Deserialize<SequenceExchange>(File.ReadAllText(dlg.FileName))
                             ?? throw new InvalidDataException("Neplatný formát souboru.");
                var name = string.IsNullOrWhiteSpace(import.Name)
                    ? $"Import {DateTime.Now:dd.MM HH:mm}"
                    : import.Name.Trim();
                var seq = _db.SaveSequence(name, import.Description ?? string.Empty, import.Steps ?? new());
                RefreshSequences(seq.Id);
                TxtFooter.Text = $"📥 Importováno do sekvence '{seq.Name}'";
            }
            catch (Exception ex)
            {
                TxtFooter.Text = $"❌ Import selhal: {ex.Message}";
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_seqIdx < 0 || _seqIdx >= _sequences.Count) return;
            var seq = _sequences[_seqIdx];

            var dlg = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = $"sequence-{seq.Name.Replace(' ', '-')}.json"
            };
            if (dlg.ShowDialog() != true) return;

            var payload = new SequenceExchange
            {
                Name = TxtSequenceName.Text.Trim(),
                Description = TxtSequenceDescription.Text.Trim(),
                Steps = _steps
            };

            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            TxtFooter.Text = $"📤 Export: {dlg.FileName}";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_dirty && MessageBox.Show("Neuložené změny. Zavřít?", "Zavřít",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            Close();
        }

        private void RenumberSteps()
        {
            for (int i = 0; i < _steps.Count; i++) _steps[i].Id = i + 1;
        }

        private void MarkDirty(object sender, TextChangedEventArgs e) => SetDirty();
        private void MarkDirtyCombo(object sender, SelectionChangedEventArgs e) => SetDirty();

        private void SetDirty(bool value = true)
        {
            if (_suppress) return;
            _dirty = value;
            BtnSaveSequence.IsEnabled = _dirty && _seqIdx >= 0;
        }

        private class SequenceExchange
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<ClickAction> Steps { get; set; } = new();
        }
    }
}
