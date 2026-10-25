using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    private CancellationTokenSource? _findCancellation;

    private void RefreshFindMatches()
    {
        _findCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _findCancellation = cancellation;
        _ = RefreshFindMatchesAsync(cancellation);
    }

    private async Task RefreshFindMatchesAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var previous = CurrentFindResult;
        foreach (var log in _findMatches)
        {
            log.IsFindMatch = false;
            log.IsCurrentFindMatch = false;
        }
        FindMatchIndex = -1;
        _findMatches = Array.Empty<LogEntryViewModel>();
        FindPositions = Array.Empty<double>();
        NotifyFindResults();
        try
        {
            var text = FindText;
            if (!IsFindVisible || string.IsNullOrWhiteSpace(text))
                return;
            var rows = Logs;
            await Task.Delay(150, token);
            var result = await Task.Run(() =>
            {
                var matches = new List<LogEntryViewModel>();
                var positions = new List<double>();
                for (var index = 0; index < rows.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var log = rows[index];
                    if (!log.TimeLabel.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                        !log.Entry.Type.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                        !log.FindText.Contains(text, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matches.Add(log);
                    positions.Add(index / (double)Math.Max(1, rows.Count - 1));
                }
                return (Matches: matches.ToArray(), Positions: positions.ToArray());
            }, token);
            token.ThrowIfCancellationRequested();
            _findMatches = result.Matches;
            FindPositions = result.Positions;
            foreach (var log in _findMatches)
                log.IsFindMatch = true;
            NotifyFindResults();
            if (_findMatches.Count > 0)
            {
                var index = previous is null ? -1 : Array.IndexOf(result.Matches, previous);
                SetFindMatch(index >= 0 ? index : 0);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(_findCancellation, cancellation))
                ErrorMessage = $"Ошибка поиска: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_findCancellation, cancellation))
                _findCancellation = null;
            cancellation.Dispose();
        }
    }

    private void NotifyFindResults()
    {
        OnPropertyChanged(nameof(FindCounter));
        OnPropertyChanged(nameof(HasFindMatches));
        NextFindCommand.NotifyCanExecuteChanged();
        PreviousFindCommand.NotifyCanExecuteChanged();
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
