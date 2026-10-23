using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Loggepector.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyList<LogEntryViewModel> _findMatches = Array.Empty<LogEntryViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFindText))]
    private bool _isFindVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFindText))]
    private string _findText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _findPositions = Array.Empty<double>();

    public string ActiveFindText => IsFindVisible ? FindText : string.Empty;
    public double CurrentFindPosition => FindMatchIndex >= 0 ? FindPositions[FindMatchIndex] : -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindCounter))]
    [NotifyPropertyChangedFor(nameof(CurrentFindResult))]
    [NotifyPropertyChangedFor(nameof(CurrentFindPosition))]
    private int _findMatchIndex = -1;

    public string FindCounter => $"{(FindMatchIndex < 0 ? 0 : FindMatchIndex + 1)} / {_findMatches.Count}";
    public bool HasFindMatches => _findMatches.Count > 0;
    public LogEntryViewModel? CurrentFindResult => FindMatchIndex >= 0 ? _findMatches[FindMatchIndex] : null;

    partial void OnFindTextChanged(string value) => RefreshFindMatches();
    partial void OnIsFindVisibleChanged(bool value) => RefreshFindMatches();
    partial void OnLogsChanged(IReadOnlyList<LogEntryViewModel> value) => RefreshFindMatches();

    private void RefreshFindMatches()
    {
        var previous = CurrentFindResult;
        foreach (var log in _findMatches)
        {
            log.IsFindMatch = false;
            log.IsCurrentFindMatch = false;
        }
        FindMatchIndex = -1;
        _findMatches = IsFindVisible && !string.IsNullOrWhiteSpace(FindText)
            ? Logs.Where(log => log.TimeLabel.Contains(FindText, StringComparison.OrdinalIgnoreCase) ||
                log.Entry.Type.Contains(FindText, StringComparison.OrdinalIgnoreCase) ||
                string.Concat(log.MessageParts.Select(part => part.Text)).Contains(FindText, StringComparison.OrdinalIgnoreCase)).ToArray()
            : Array.Empty<LogEntryViewModel>();
        foreach (var log in _findMatches)
            log.IsFindMatch = true;
        if (_findMatches.Count == 0)
            FindPositions = Array.Empty<double>();
        else
        {
            var indices = Logs.Select((log, index) => (log.Entry.LineNumber, Index: index))
                .ToDictionary(item => item.LineNumber, item => item.Index);
            FindPositions = _findMatches.Select(log => indices[log.Entry.LineNumber] / (double)Math.Max(1, Logs.Count - 1)).ToArray();
        }
        OnPropertyChanged(nameof(FindCounter));
        OnPropertyChanged(nameof(HasFindMatches));
        NextFindCommand.NotifyCanExecuteChanged();
        PreviousFindCommand.NotifyCanExecuteChanged();
        if (_findMatches.Count > 0)
        {
            var index = previous is null ? -1 : Array.IndexOf(_findMatches.ToArray(), previous);
            SetFindMatch(index >= 0 ? index : 0);
        }
    }

    private void SetFindMatch(int index)
    {
        if (CurrentFindResult is { } previous)
            previous.IsCurrentFindMatch = false;
        if (FindMatchIndex == index)
            OnPropertyChanged(nameof(CurrentFindResult));
        else
            FindMatchIndex = index;
        CurrentFindResult!.IsCurrentFindMatch = true;
    }

    [RelayCommand(CanExecute = nameof(HasFindMatches))]
    private void NextFind() => SetFindMatch((FindMatchIndex + 1) % _findMatches.Count);

    [RelayCommand(CanExecute = nameof(HasFindMatches))]
    private void PreviousFind() => SetFindMatch((FindMatchIndex - 1 + _findMatches.Count) % _findMatches.Count);

    [RelayCommand]
    private void CloseFind() => IsFindVisible = false;
}
