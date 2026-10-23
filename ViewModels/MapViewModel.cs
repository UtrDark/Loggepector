using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loggepector.Models;
using Loggepector.Services;

namespace Loggepector.ViewModels;

public sealed partial class MapViewModel(string mapName, IMapViewService mapViewService, IMapPreparationService mapPreparation) : ViewModelBase
{
    public required MainWindowViewModel Timeline { get; init; }

    private int _logRevision;

    [ObservableProperty]
    private MapLogIndex? _logIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPoint))]
    private MapLogPoint? _selectedPoint;

    [ObservableProperty]
    private IReadOnlyList<LogEntryViewModel> _pointEntries = Array.Empty<LogEntryViewModel>();

    public bool HasSelectedPoint => SelectedPoint is not null;

    private static string NormalizeMapName(string? name) =>
        new((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public async Task UpdateLogsAsync(IReadOnlyList<ILogEntry> entries)
    {
        var revision = ++_logRevision;
        try
        {
            var matchingMap = Timeline.MapName is { } logMap && NormalizeMapName(MapName) == NormalizeMapName(logMap);
            var lavaland = NormalizeMapName(MapName) == NormalizeMapName(AdditionalMaps.LavalandName);
            var planetaryPort = NormalizeMapName(MapName) == NormalizeMapName(AdditionalMaps.PlanetaryPortName);
            var index = await Task.Run(() => new MapLogIndex(entries.Where(entry =>
                planetaryPort ? AdditionalMaps.IsPlanetaryPort(entry) :
                lavaland ? AdditionalMaps.IsLavaland(entry) :
                matchingMap && !AdditionalMaps.IsLavaland(entry) && !AdditionalMaps.IsPlanetaryPort(entry)).ToArray()));
            if (revision != _logRevision)
                return;
            LogIndex = index;
            ClosePoint();
        }
        catch (Exception exception)
        {
            if (revision == _logRevision)
                Status = $"Ошибка маркеров: {exception.Message}";
        }
    }

    public void ShowPoint(MapLogPoint point)
    {
        if (SelectedPoint is { } selected && selected.X == point.X && selected.Y == point.Y && selected.Z == point.Z)
        {
            ClosePoint();
            return;
        }
        PointEntries = point.Entries.Select(entry => new LogEntryViewModel(entry, Timeline.RoundStartTime, Timeline.Relative, Timeline.ShowArea)).ToArray();
        SelectedPoint = point;
    }

    [RelayCommand]
    private void ClosePoint()
    {
        SelectedPoint = null;
        PointEntries = Array.Empty<LogEntryViewModel>();
    }

    public void UpdateTimeDisplay()
    {
        foreach (var entry in PointEntries)
        {
            entry.Relative = Timeline.Relative;
            entry.ShowArea = Timeline.ShowArea;
        }
    }

    [ObservableProperty]
    private string _mapName = mapName;

    [ObservableProperty]
    private IReadOnlyList<string> _availableMaps = Array.Empty<string>();

    private CancellationToken _lifetime;
    private CancellationTokenSource? _mapLoadCancellation;
    private readonly List<Task> _mapLoads = new();
    public Func<Task>? ReleaseMapResourcesAsync { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveMapToolsCommand))]
    private bool _hasMapTools;

    public void RefreshMapToolsState() => HasMapTools = mapPreparation.HasLocalData;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveMapToolsCommand))]
    private bool _isRemovingMapTools;

    private bool CanRemoveMapTools => HasMapTools && !IsRemovingMapTools;

    [RelayCommand(CanExecute = nameof(CanRemoveMapTools))]
    private async Task RemoveMapTools(CancellationToken cancellationToken)
    {
        IsRemovingMapTools = true;
        Status = "Удаление данных карт…";
        try
        {
            _mapLoadCancellation?.Cancel();
            Timeline.CancelMapToolsCommand.Execute(null);
            await Task.WhenAll(_mapLoads.ToArray());
            _logRevision++;
            ClosePoint();
            MapName = string.Empty;
            AvailableMaps = Array.Empty<string>();
            Layers = Array.Empty<int>();
            LogIndex = null;
            Document = null;
            FocusLocation = null;
            var release = ReleaseMapResourcesAsync
                ?? throw new InvalidOperationException("Не подключено освобождение ресурсов карты.");
            await release();
            await Timeline.RemoveMapToolsAsync(cancellationToken);
            Status = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Удаление данных карт отменено.";
        }
        catch (Exception exception)
        {
            Status = $"Не удалось удалить данные карт: {exception.Message}";
        }
        finally
        {
            IsRemovingMapTools = false;
            RefreshMapToolsState();
        }
    }

    private Task StartMapLoad()
    {
        _mapLoads.RemoveAll(task => task.IsCompleted);
        var task = LoadSelectedMapAsync();
        _mapLoads.Add(task);
        return task;
    }

    partial void OnMapNameChanged(string value)
    {
        if (IsRemovingMapTools && !string.IsNullOrWhiteSpace(value))
            return;
        if (string.IsNullOrWhiteSpace(value))
        {
            _mapLoadCancellation?.Cancel();
            Document = null;
            return;
        }
        FocusLocation = null;
        ClosePoint();
        _ = UpdateLogsAsync(Timeline.Logs.Select(log => log.Entry).ToArray());
        _ = StartMapLoad();
    }

    [RelayCommand]
    private void FocusLogTime(LogEntryViewModel entry) => Timeline.FocusLogTime(entry.Entry, (double)TimeCaptureRadius);

    [ObservableProperty]
    private decimal _timeCaptureRadius = 3;

    [ObservableProperty]
    private MapLocation? _focusLocation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(LayerDownCommand))]
    private MapDocument? _document;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(LayerDownCommand))]
    private IReadOnlyList<int> _layers = Array.Empty<int>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(LayerDownCommand))]
    private int _selectedLayer = 1;

    [ObservableProperty]
    private double _zoom = 0.125;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    private int _statusRevision;

    partial void OnStatusChanged(string value) => _statusRevision++;

    private async Task ShowCoordinateWarningAsync(MapLocation location)
    {
        Status = $"Координаты ({location.X}, {location.Y}, {location.Z}) находятся вне карты станции.";
        var revision = ++_statusRevision;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), _lifetime);
            if (revision == _statusRevision)
                Status = string.Empty;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(LayerDownCommand))]
    private bool _isLoading;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    private bool CanLayerUp => Document is not null && !IsLoading && Layers.Any(z => z > SelectedLayer);
    private bool CanLayerDown => Document is not null && !IsLoading && Layers.Any(z => z < SelectedLayer);

    [RelayCommand]
    private async Task EnableMapTools(CancellationToken cancellationToken)
    {
        try
        {
            await Timeline.EnableMapToolsCommand.ExecuteAsync(null);
            if (Timeline.AreMapsReady)
                await LoadAsync(_lifetime);
        }
        finally
        {
            RefreshMapToolsState();
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _lifetime = cancellationToken;
        RefreshMapToolsState();
        try
        {
            var maps = await mapPreparation.GetMapsAsync(cancellationToken);
            if (IsRemovingMapTools)
                return;
            AvailableMaps = maps;
            if (!string.IsNullOrWhiteSpace(MapName))
                await StartMapLoad();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    private async Task LoadSelectedMapAsync()
    {
        _mapLoadCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _mapLoadCancellation = cancellation;
        var selectedMap = MapName;
        IsLoading = true;
        Document = null;
        Status = "Подготовка выбранной карты…";
        try
        {
            var progress = new Progress<string>(value =>
            {
                if (ReferenceEquals(_mapLoadCancellation, cancellation))
                    Status = value;
            });
            await Task.Run(() => mapPreparation.RenderAsync(selectedMap, progress, cancellation.Token), cancellation.Token);
            var document = await Task.Run(async () =>
            {
                var loaded = await mapViewService.LoadAsync(selectedMap, cancellation.Token);
                if (NormalizeMapName(selectedMap) == NormalizeMapName(AdditionalMaps.LavalandName))
                {
                    var worldLayer = Timeline.MapName is { Length: > 0 } station
                        ? await mapViewService.GetStationLayerCountAsync(station, cancellation.Token) + 2
                        : loaded.DefaultLayer;
                    loaded = loaded with { WorldZOffset = worldLayer - loaded.DefaultLayer };
                }
                return loaded;
            }, cancellation.Token);
            if (!ReferenceEquals(_mapLoadCancellation, cancellation))
                return;
            Layers = document.Layers;
            SelectedLayer = document.DefaultLayer;
            Document = document;
            Status = string.Empty;
            ApplyFocus();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_mapLoadCancellation, cancellation))
                Status = "Загрузка карты отменена.";
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_mapLoadCancellation, cancellation))
                Status = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_mapLoadCancellation, cancellation))
            {
                _mapLoadCancellation = null;
                IsLoading = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLayerUp))]
    private void LayerUp()
    {
        if (CanLayerUp)
            SelectedLayer = Layers.First(z => z > SelectedLayer);
    }

    [RelayCommand(CanExecute = nameof(CanLayerDown))]
    private void LayerDown()
    {
        if (CanLayerDown)
            SelectedLayer = Layers.Last(z => z < SelectedLayer);
    }

    partial void OnFocusLocationChanged(MapLocation? value) => ApplyFocus();

    private void ApplyFocus()
    {
        if (Document is not { } map || FocusLocation is not { } location)
            return;
        var layer = location.Z - map.WorldZOffset;
        if (!Layers.Contains(layer) || location.X < 1 || location.Y < 1 ||
            location.X > map.Width / map.TileSize || location.Y > map.Height / map.TileSize)
        {
            _ = ShowCoordinateWarningAsync(location);
            return;
        }
        SelectedLayer = layer;
        Status = string.Empty;
    }
}
