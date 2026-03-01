using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace D3Energy.UI.Automation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isTransportEnabled = true;

    public MainViewModel()
    {
        TogglePlayCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(TogglePlay, CanTogglePlay);
    }

    public IRelayCommand TogglePlayCommand { get; }

    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;
    }

    private bool CanTogglePlay() => IsTransportEnabled;

    partial void OnIsTransportEnabledChanged(bool value)
    {
        TogglePlayCommand.NotifyCanExecuteChanged();
    }
}
