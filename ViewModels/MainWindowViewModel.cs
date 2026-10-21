using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Loggepector.Models;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using Loggepector.Services;
using Loggepector.Utils;

namespace Loggepector.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<LogEntryViewModel> SelectedLogs { get; } = new();
    private int _contextRevision;

    [ObservableProperty]
    private bool _captureSameType;

    [ObservableProperty]
    private bool _captureByCoordinates;

    [ObservableProperty]
    private decimal _neighborCount;

    [ObservableProperty]
    private decimal _coordinateRadius;

    [ObservableProperty]
    private bool _isCapturingContext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextStatus))]
    private string _contextStatus = string.Empty;

    public bool HasContextStatus => !string.IsNullOrEmpty(ContextStatus);

    private readonly ILogService _logService;
    private readonly IMapPreparationService _mapPreparation;
    private readonly IMapViewService _mapViewService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenMap))]
    private string? _mapName;

    [ObservableProperty]
    private bool _isMapToolsPromptVisible;

    [ObservableProperty]
    private string _mapToolsStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableMapToolsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableMapToolsCommand))]
    private bool _isMapToolsBusy;

    private bool CanChooseMapTools => !IsMapToolsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenFile))]
    [NotifyPropertyChangedFor(nameof(CanOpenMap))]
    [NotifyPropertyChangedFor(nameof(CanShowPlayersInfo))]
    [NotifyPropertyChangedFor(nameof(CanSelectTime))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectTime))]
    [NotifyPropertyChangedFor(nameof(CanShowPlayersInfo))]
    private bool _hasLogs;

    [ObservableProperty]
    private int _loadProgress;

    [ObservableProperty]
    private string _fileName = "Файл не выбран";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    private double _fromSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    private double _toSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    [NotifyPropertyChangedFor(nameof(SliderMaximum))]
    [NotifyPropertyChangedFor(nameof(CanSelectTime))]
    private double _maximumSecond;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountLabel))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<LogEntryViewModel> _logs = Array.Empty<LogEntryViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LogTypeViewModel> _logTypes = Array.Empty<LogTypeViewModel>();

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredPlayers))]
    [NotifyPropertyChangedFor(nameof(PlayerLabel))]
    private IReadOnlyList<PlayerViewModel> _players = Array.Empty<PlayerViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredPlayers))]
    private string _playerSearchText = string.Empty;

    public IEnumerable<PlayerViewModel> FilteredPlayers =>
        Players.Where(player => player.CKey.Contains(CKey.Normalize(PlayerSearchText), StringComparison.Ordinal));

    public string PlayerLabel
    {
        get
        {
            var selected = Players.Where(player => player.IsSelected).Select(player => player.CKey).ToArray();
            return selected.Length == 0 ? "Игроки: все ▾" : "Игроки: " + string.Join(", ", selected) + " ▾";
        }
    }

    public DateTime RoundStartTime => _logService.StartTime;

    public bool IsEmpty => Logs.Count == 0;
    public bool CanOpenFile => !IsLoading;
    public bool CanOpenMap => !IsLoading && !string.IsNullOrWhiteSpace(MapName);
    public bool CanShowPlayersInfo => HasLogs && !IsLoading;
    public bool CanSelectTime => HasLogs && !IsLoading && MaximumSecond > 0;
    public double SliderMaximum => Math.Max(1, MaximumSecond);
    public string TimeLabel => $"От {TimeSpan.FromSeconds(FromSecond):c} до {TimeSpan.FromSeconds(ToSecond):c} · Всего {TimeSpan.FromSeconds(MaximumSecond):c}";
    public string CountLabel => $"Показано: {Logs.Count:N0} · Всего: {TotalCount:N0}";

    public MainWindowViewModel(ILogService logService, IMapPreparationService mapPreparation, IMapViewService mapViewService)
    {
        _logService = logService;
        _mapPreparation = mapPreparation;
        _mapViewService = mapViewService;
    }

    public async Task InitializeMapToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var choice = await _mapPreparation.GetChoiceAsync(cancellationToken);
            IsMapToolsPromptVisible = choice is null;
            if (choice == true)
                await PrepareMapsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MapToolsStatus = "Подготовка карт остановлена.";
        }
        catch (Exception exception)
        {
            MapToolsStatus = $"Ошибка подготовки карт: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseMapTools))]
    private Task EnableMapTools(CancellationToken cancellationToken) => SetMapToolsChoiceAsync(true, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanChooseMapTools))]
    private Task DisableMapTools(CancellationToken cancellationToken) => SetMapToolsChoiceAsync(false, cancellationToken);

    private async Task SetMapToolsChoiceAsync(bool enabled, CancellationToken cancellationToken)
    {
        IsMapToolsBusy = true;
        try
        {
            await _mapPreparation.SaveChoiceAsync(enabled, cancellationToken);
            IsMapToolsPromptVisible = false;
            if (enabled)
                await PrepareMapsAsync(cancellationToken);
            else
                MapToolsStatus = "Инструменты карт отключены.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MapToolsStatus = "Подготовка карт остановлена.";
        }
        catch (Exception exception)
        {
            MapToolsStatus = $"Ошибка подготовки карт: {exception.Message}";
        }
        finally
        {
            IsMapToolsBusy = false;
        }
    }

    private async Task PrepareMapsAsync(CancellationToken cancellationToken)
    {
        IsMapToolsBusy = true;
        try
        {
            var progress = new Progress<string>(status => MapToolsStatus = status);
            await Task.Run(() => _mapPreparation.PrepareAsync(progress, cancellationToken), cancellationToken);
        }
        finally
        {
            IsMapToolsBusy = false;
        }
    }

    public PlayersInfoViewModel CreatePlayersInfo() => new(_logService, TogglePlayer, GetPlayer);

    public MapViewModel CreateMapView() => new(MapName
        ?? throw new InvalidOperationException("В логе не найдена запись о загрузке карты."), _mapViewService) { Timeline = this };

    private void TogglePlayer(string cKey)
    {
        if (!CanShowPlayersInfo)
            return;

        var player = GetPlayer(cKey);
        player.IsSelected = !player.IsSelected;
    }

    private PlayerViewModel GetPlayer(string cKey)
    {
        var key = CKey.Normalize(cKey);
        var player = Players.FirstOrDefault(item => item.CKey == key);
        if (player is null)
        {
            player = new PlayerViewModel(key, OnPlayerSelectionChanged);
            Players = Players.Append(player).OrderBy(item => item.CKey, StringComparer.Ordinal).ToArray();
        }

        return player;
    }

    partial void OnNeighborCountChanged(decimal value) => RefreshContext();

    partial void OnCoordinateRadiusChanged(decimal value) => RefreshContext();

    partial void OnCaptureSameTypeChanged(bool value) => RefreshContext();

    partial void OnCaptureByCoordinatesChanged(bool value) => RefreshContext();

    private void RefreshContext()
    {
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    private void ResetContext()
    {
        _contextRevision++;
        ContextStatus = string.Empty;
        IsCapturingContext = false;
    }

    public async Task LoadAsync(string path)
    {
        IsLoading = true;
        ResetContext();
        SelectedLogs.Clear();
        ErrorMessage = null;
        LoadProgress = 0;
        var progress = new Progress<int>(value =>
        {
            if (IsLoading)
                LoadProgress = value;
        });

        try
        {
            await _logService.LoadAsync(path, progress: progress);
            FileName = Path.GetFileName(path);
            TotalCount = _logService.Count;
            MapName = _logService.MapName;
            HasLogs = TotalCount > 0;
            LogTypes = _logService.GetTypes().Select(type => new LogTypeViewModel(type, UpdateLogs)).ToArray();
            Players = _logService.GetCKeys().Select(key => new PlayerViewModel(key, OnPlayerSelectionChanged)).ToArray();
            PlayerSearchText = string.Empty;
            FromSecond = 0;
            ToSecond = 0;
            MaximumSecond = Math.Ceiling(_logService.Duration.TotalSeconds);
            ToSecond = MaximumSecond;
            UpdateLogs();
            LoadProgress = 100;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnFromSecondChanged(double value)
    {
        if (CanSelectTime)
            UpdateLogs();
    }

    partial void OnToSecondChanged(double value)
    {
        if (CanSelectTime)
            UpdateLogs();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    private void OnPlayerSelectionChanged()
    {
        OnPropertyChanged(nameof(PlayerLabel));
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    private void UpdateLogs() => _ = UpdateLogsAsync();

    private async Task UpdateLogsAsync()
    {
        var revision = ++_contextRevision;
        var from = TimeSpan.FromSeconds(FromSecond);
        var to = TimeSpan.FromSeconds(ToSecond);
        var types = LogTypes.Where(type => type.IsSelected).Select(type => type.Type).ToArray();
        var players = Players.Where(player => player.IsSelected).Select(player => player.CKey).ToArray();
        var capture = CaptureSameType || CaptureByCoordinates;
        var sameType = CaptureSameType;
        var byCoordinates = CaptureByCoordinates;
        var count = (int)Math.Clamp(NeighborCount, 0, 100);
        var radius = (double)Math.Clamp(CoordinateRadius, 0, 255);
        IsCapturingContext = capture;
        ErrorMessage = null;
        try
        {
            var filtered = _logService.GetLogs(from, to, types, players, SearchText);
            IReadOnlyList<ILogEntry> entries = filtered;
            if (capture)
            {
                var candidates = _logService.GetLogs(from, to);
                entries = await Task.Run(() => LogNeighbors.Find(candidates, filtered,
                    sameType, byCoordinates, count, radius));
            }
            if (revision != _contextRevision)
                return;
            var previous = Logs.ToDictionary(log => log.Entry.LineNumber);
            var selected = SelectedLogs.Select(log => log.Entry.LineNumber).ToHashSet();
            Logs = entries.Select(entry => previous.TryGetValue(entry.LineNumber, out var log) && ReferenceEquals(log.Entry, entry)
                ? log : new LogEntryViewModel(entry, RoundStartTime)).ToArray();
            SelectedLogs.Clear();
            foreach (var log in Logs.Where(log => selected.Contains(log.Entry.LineNumber)))
                SelectedLogs.Add(log);
            ContextStatus = capture
                ? $"По фильтрам: {filtered.Count}. Добавлено соседних: {entries.Count - filtered.Count}."
                : string.Empty;
        }
        catch (Exception exception)
        {
            if (revision == _contextRevision)
                ErrorMessage = exception.Message;
        }
        finally
        {
            if (revision == _contextRevision)
                IsCapturingContext = false;
        }
    }
}
