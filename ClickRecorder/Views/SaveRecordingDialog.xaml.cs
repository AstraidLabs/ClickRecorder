using System.Windows;

namespace ClickRecorder.Views;

public partial class SaveRecordingDialog : Window
{
    public SaveRecordingDialog(string defaultName, string defaultDescription)
    {
        InitializeComponent();
        TxtName.Text = defaultName;
        TxtDescription.Text = defaultDescription;
    }

    public bool ShouldSave { get; private set; }
    public bool ShouldDiscard { get; private set; }
    public string SequenceName => TxtName.Text?.Trim() ?? string.Empty;
    public string SequenceDescription => TxtDescription.Text?.Trim() ?? string.Empty;

    private void BtnChooseSave_Click(object sender, RoutedEventArgs e)
    {
        SaveForm.Visibility = Visibility.Visible;
        BtnSave.Visibility = Visibility.Visible;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SequenceName))
        {
            MessageBox.Show("Název je povinný.", "Uložení", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShouldSave = true;
        DialogResult = true;
    }

    private void BtnDiscard_Click(object sender, RoutedEventArgs e)
    {
        ShouldDiscard = true;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
