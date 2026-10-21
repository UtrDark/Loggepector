using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Loggepector.ViewModels;

public sealed partial class PlayerInfoViewModel(string title, string text, string subtitle, IBrush accent,
    string cKey, Action<string> selectPlayer, PlayerViewModel selection) : ViewModelBase
{
    [ObservableProperty]
    private bool _isSearchMatch;

    public string Title { get; } = title;
    public string Text { get; } = text;
    public string Subtitle { get; } = subtitle;
    public IBrush Accent { get; } = accent;
    public string CKey { get; } = cKey;
    public PlayerViewModel Selection { get; } = selection;

    [RelayCommand]
    private void FilterPlayer() => selectPlayer(CKey);
}
