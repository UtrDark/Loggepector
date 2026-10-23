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
    private int _copyNoticeRevision;

    [ObservableProperty]
    private bool _isCopyNoticeVisible;

    public async Task ShowCopyNoticeAsync()
    {
        var revision = ++_copyNoticeRevision;
        IsCopyNoticeVisible = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (revision == _copyNoticeRevision)
            IsCopyNoticeVisible = false;
    }

    public ObservableCollection<SearchBadgeViewModel> SearchBadges { get; } = new();

    [ObservableProperty]
    private bool _fuzzySearch;

    partial void OnFuzzySearchChanged(bool value)
    {
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    [RelayCommand]
    private void SearchBadge(BadgeSearchRequest request)
    {
        IsSearchVisible = true;
        if (!request.Add)
        {
            SearchBadges.Clear();
            SearchText = string.Empty;
        }
        if (!SearchBadges.Any(badge => badge.Part.Kind == request.Part.Kind &&
            badge.Part.Text.Equals(request.Part.Text, StringComparison.OrdinalIgnoreCase)))
            SearchBadges.Add(new SearchBadgeViewModel(request.Part));
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    [RelayCommand]
    private void RemoveSearchBadge(SearchBadgeViewModel badge)
    {
        SearchBadges.Remove(badge);
        if (HasLogs && !IsLoading)
            UpdateLogs();
    }

    [ObservableProperty]
    private bool _sanitize = true;

    [ObservableProperty]
    private bool _showRaw;

    [ObservableProperty]
    private bool _showArea;

    partial void OnShowAreaChanged(bool value)
    {
        foreach (var log in Logs.Concat(SelectedLogs).Distinct())
            log.ShowArea = value;
        RefreshFindMatches();
    }

    [ObservableProperty]
    private bool _relative = true;

    partial void OnRelativeChanged(bool value)
    {
        foreach (var log in Logs.Concat(SelectedLogs).Distinct())
            log.Relative = value;
        RefreshFindMatches();
    }

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

    private readonly ILogisService _logisService;
    private LogisBrowserViewModel? _logisBrowser;
    private Uri? _logisBrowserAddress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogisButtonText))]
    private bool _hasLogisSession;

    public string LogisButtonText => HasLogisSession ? "Открыть из Logis" : "Подключить Logis";
    public LogisViewModel CreateLogisConnection() => new(_logisService);
    public LogisBrowserViewModel CreateLogisBrowser(bool allowAutomaticLogin)
    {
        if (_logisBrowser is null || _logisBrowserAddress != _logisService.ServerAddress)
        {
            _logisBrowser = new LogisBrowserViewModel(_logisService, allowAutomaticLogin);
            _logisBrowserAddress = _logisService.ServerAddress;
        }
        _logisBrowser.PrepareOpen(allowAutomaticLogin);
        return _logisBrowser;
    }
    public void RestoreLogisSession()
    {
        _logisService.RestoreSession();
        RefreshLogisSession();
    }
    public void RefreshLogisSession() => HasLogisSession = _logisService.SessionId is not null;

    private readonly IThemeService _themeService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeButtonText))]
    private bool _isDarkTheme;

    public string ThemeButtonText => IsDarkTheme ? "☀" : "☾";

    [RelayCommand]
    private void ToggleTheme()
    {
        try
        {
            _themeService.SetTheme(!IsDarkTheme);
            IsDarkTheme = _themeService.IsDark;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Не удалось сохранить тему: {exception.Message}";
        }
    }

    private readonly ILogService _logService;
    private readonly IMapPreparationService _mapPreparation;
    private readonly IMapViewService _mapViewService;
    private CancellationTokenSource? _mapToolsCancellation;
    private Task _mapPreparationTask = Task.CompletedTask;
    private bool _mapToolsDeleted;

    [ObservableProperty]
    private string _mapRepositoryUrl = new MapToolsSettings(null).RepositoryUrl;

    partial void OnMapRepositoryUrlChanged(string value) => AreMapsReady = false;
    public Func<MapDownloadStage, CancellationToken, Task<bool>>? ConfirmMapDownloadAsync { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenMap))]
    private string? _mapName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapToolsStatus))]
    private string _mapToolsStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableMapToolsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelMapToolsCommand))]
    private bool _isMapToolsBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableMapToolsCommand))]
    private bool _areMapsReady;

    public bool HasMapToolsStatus => !string.IsNullOrWhiteSpace(MapToolsStatus);

    private bool CanChooseMapTools => !IsMapToolsBusy && !AreMapsReady;

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
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

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
    public bool CanOpenMap => !IsLoading;
    public bool CanShowPlayersInfo => HasLogs && !IsLoading;
    public bool CanSelectTime => HasLogs && !IsLoading && MaximumSecond > 0;
    public double SliderMaximum => Math.Max(1, MaximumSecond);
    public string TimeLabel => $"От {TimeSpan.FromSeconds(FromSecond):c} до {TimeSpan.FromSeconds(ToSecond):c} · Всего {TimeSpan.FromSeconds(MaximumSecond):c}";
    public string CountLabel => $"Показано: {Logs.Count:N0} · Всего: {TotalCount:N0}";

    public MainWindowViewModel(ILogService logService, IMapPreparationService mapPreparation, IMapViewService mapViewService, IThemeService themeService, ILogisService logisService)
    {
        _logisService = logisService;
        _themeService = themeService;
        IsDarkTheme = themeService.IsDark;
        _logService = logService;
        _mapPreparation = mapPreparation;
        _mapViewService = mapViewService;
    }

    public async Task InitializeMapToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            MapRepositoryUrl = await _mapPreparation.GetRepositoryAsync(cancellationToken);
            var choice = await _mapPreparation.GetChoiceAsync(cancellationToken);
            if (choice == true && !_mapToolsDeleted)
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
    private async Task EnableMapTools(CancellationToken cancellationToken)
    {
        _mapToolsDeleted = false;
        IsMapToolsBusy = true;
        try
        {
            await _mapPreparation.SaveChoiceAsync(true, MapRepositoryUrl, cancellationToken);
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
        finally
        {
            IsMapToolsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsMapToolsBusy))]
    private void CancelMapTools()
    {
        _mapToolsCancellation?.Cancel();
        EnableMapToolsCommand.Cancel();
    }

    private Task PrepareMapsAsync(CancellationToken cancellationToken) =>
        _mapPreparationTask = RunPrepareMapsAsync(cancellationToken);

    public async Task RemoveMapToolsAsync(CancellationToken cancellationToken)
    {
        _mapToolsDeleted = true;
        CancelMapTools();
        if (EnableMapToolsCommand.ExecutionTask is { } enabling)
            await enabling;
        await _mapPreparationTask;
        IsMapToolsBusy = true;
        AreMapsReady = false;
        try
        {
            await _mapPreparation.DeleteAllAsync(cancellationToken);
            MapRepositoryUrl = new MapToolsSettings(null).RepositoryUrl;
            MapToolsStatus = "Все данные карт удалены.";
        }
        finally
        {
            IsMapToolsBusy = false;
        }
    }

    private async Task RunPrepareMapsAsync(CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _mapToolsCancellation = cancellation;
        AreMapsReady = false;
        IsMapToolsBusy = true;
        try
        {
            var progress = new Progress<string>(status => MapToolsStatus = status);
            async Task ConfirmDownload(MapDownloadStage stage, CancellationToken token)
            {
                var confirm = ConfirmMapDownloadAsync
                    ?? throw new InvalidOperationException("Не подключено окно подтверждения скачивания карт.");
                if (!await confirm(stage, token))
                    cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            await Task.Run(() => _mapPreparation.PrepareAsync(progress, ConfirmDownload, cancellation.Token), cancellation.Token);
            AreMapsReady = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            MapToolsStatus = "Подготовка карт отменена.";
        }
        catch (Exception exception)
        {
            MapToolsStatus = $"Ошибка подготовки карт: {exception.Message}";
        }
        finally
        {
            _mapToolsCancellation = null;
            IsMapToolsBusy = false;
        }
    }

    [RelayCommand]
    private void ResetTimeSlice()
    {
        FromSecond = 0;
        ToSecond = MaximumSecond;
    }

    public void FocusLogTime(ILogEntry entry, double radius)
    {
        var second = (entry.Timestamp - RoundStartTime).TotalSeconds;
        FromSecond = 0;
        ToSecond = Math.Clamp(second + radius, 0, MaximumSecond);
        FromSecond = Math.Clamp(second - radius, 0, ToSecond);
    }

    public PlayersInfoViewModel CreatePlayersInfo() => new(_logService, TogglePlayer, GetPlayer);

    public MapViewModel CreateMapView() => new(MapName ?? string.Empty, _mapViewService, _mapPreparation) { Timeline = this };

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
        var types = LogTypes.Where(type => type.IsSelected == true).Select(type => type.Type).ToArray();
        var excludedTypes = LogTypes.Where(type => type.IsSelected is null).Select(type => type.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            IReadOnlyList<ILogEntry> filtered = _logService.GetLogs(from, to, types, players, SearchText,
                    FuzzySearch, SearchBadges.Select(badge => badge.Part).ToArray())
                .Where(entry => !excludedTypes.Contains(entry.Type)).ToArray();
            IReadOnlyList<ILogEntry> entries = filtered;
            if (capture)
            {
                var candidates = _logService.GetLogs(from, to);
                entries = await Task.Run(() => LogNeighbors.Find(candidates, filtered,
                    sameType, byCoordinates, count, radius));
            }
            if (excludedTypes.Count > 0)
                entries = entries.Where(entry => !excludedTypes.Contains(entry.Type)).ToArray();
            if (revision != _contextRevision)
                return;
            var previous = Logs.ToDictionary(log => log.Entry.LineNumber);
            var selected = SelectedLogs.Select(log => log.Entry.LineNumber).ToHashSet();
            Logs = entries.Select((entry, index) =>
            {
                var row = previous.TryGetValue(entry.LineNumber, out var log) && ReferenceEquals(log.Entry, entry)
                    ? log : new LogEntryViewModel(entry, RoundStartTime, Relative, ShowArea);
                row.IsAlternate = index % 2 != 0;
                return row;
            }).ToArray();
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
