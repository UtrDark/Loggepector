using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Loggepector.ViewModels;

public partial class MapViewModel
{
    private IReadOnlyList<LogEntryViewModel> _pointFindMatches = Array.Empty<LogEntryViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePointFindText))]
    private bool _isPointFindVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePointFindText))]
    private string _pointFindText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _pointFindPositions = Array.Empty<double>();

    public string ActivePointFindText => IsPointFindVisible ? PointFindText : string.Empty;
    public double CurrentPointFindPosition => PointFindMatchIndex >= 0 ? PointFindPositions[PointFindMatchIndex] : -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PointFindCounter))]
    [NotifyPropertyChangedFor(nameof(CurrentPointFindResult))]
    [NotifyPropertyChangedFor(nameof(CurrentPointFindPosition))]
    private int _pointFindMatchIndex = -1;

    public string PointFindCounter => $"{(PointFindMatchIndex < 0 ? 0 : PointFindMatchIndex + 1)} / {_pointFindMatches.Count}";
    public bool HasPointFindMatches => _pointFindMatches.Count > 0;
    public LogEntryViewModel? CurrentPointFindResult => PointFindMatchIndex >= 0 ? _pointFindMatches[PointFindMatchIndex] : null;

    partial void OnPointFindTextChanged(string value) => RefreshPointFindMatches();
    partial void OnIsPointFindVisibleChanged(bool value) => RefreshPointFindMatches();
    partial void OnPointEntriesChanged(IReadOnlyList<LogEntryViewModel> value) => RefreshPointFindMatches();

    private CancellationTokenSource? _pointFindCancellation;

    private void RefreshPointFindMatches()
    {
        _pointFindCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _pointFindCancellation = cancellation;
        _ = RefreshPointFindMatchesAsync(cancellation);
    }

    private async Task RefreshPointFindMatchesAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var previous = CurrentPointFindResult;
        foreach (var log in _pointFindMatches)
        {
            log.IsFindMatch = false;
            log.IsCurrentFindMatch = false;
        }
        PointFindMatchIndex = -1;
        _pointFindMatches = Array.Empty<LogEntryViewModel>();
        PointFindPositions = Array.Empty<double>();
        NotifyPointFindResults();
        try
        {
            var text = PointFindText;
            if (!IsPointFindVisible || string.IsNullOrWhiteSpace(text))
                return;
            var rows = PointEntries;
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
            _pointFindMatches = result.Matches;
            PointFindPositions = result.Positions;
            foreach (var log in _pointFindMatches)
                log.IsFindMatch = true;
            NotifyPointFindResults();
            if (_pointFindMatches.Count > 0)
            {
                var index = previous is null ? -1 : Array.IndexOf(result.Matches, previous);
                SetPointFindMatch(index >= 0 ? index : 0);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(_pointFindCancellation, cancellation))
                Status = $"Ошибка поиска: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_pointFindCancellation, cancellation))
                _pointFindCancellation = null;
            cancellation.Dispose();
        }
    }

    private void NotifyPointFindResults()
    {
        OnPropertyChanged(nameof(PointFindCounter));
        OnPropertyChanged(nameof(HasPointFindMatches));
        NextPointFindCommand.NotifyCanExecuteChanged();
        PreviousPointFindCommand.NotifyCanExecuteChanged();
    }

    private void SetPointFindMatch(int index)
    {
        if (CurrentPointFindResult is { } previous)
            previous.IsCurrentFindMatch = false;
        if (PointFindMatchIndex == index)
            OnPropertyChanged(nameof(CurrentPointFindResult));
        else
            PointFindMatchIndex = index;
        CurrentPointFindResult!.IsCurrentFindMatch = true;
    }

    [RelayCommand(CanExecute = nameof(HasPointFindMatches))]
    private void NextPointFind() => SetPointFindMatch((PointFindMatchIndex + 1) % _pointFindMatches.Count);

    [RelayCommand(CanExecute = nameof(HasPointFindMatches))]
    private void PreviousPointFind() => SetPointFindMatch((PointFindMatchIndex - 1 + _pointFindMatches.Count) % _pointFindMatches.Count);

    [RelayCommand]
    private void ClosePointFind() => IsPointFindVisible = false;
}
