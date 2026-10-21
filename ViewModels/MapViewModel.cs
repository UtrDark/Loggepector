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

public sealed partial class MapViewModel(string mapName, IMapViewService mapViewService) : ViewModelBase
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

    public async Task UpdateLogsAsync(IReadOnlyList<ILogEntry> entries)
    {
        var revision = ++_logRevision;
        try
        {
            var index = await Task.Run(() => new MapLogIndex(entries));
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
        PointEntries = point.Entries.Select(entry => new LogEntryViewModel(entry, Timeline.RoundStartTime)).ToArray();
        SelectedPoint = point;
    }

    [RelayCommand]
    private void ClosePoint()
    {
        SelectedPoint = null;
        PointEntries = Array.Empty<LogEntryViewModel>();
    }

    public string MapName { get; } = mapName;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(LayerDownCommand))]
    private bool _isLoading;

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    private bool CanLayerUp => Document is not null && !IsLoading && Layers.Any(z => z > SelectedLayer);
    private bool CanLayerDown => Document is not null && !IsLoading && Layers.Any(z => z < SelectedLayer);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        Document = null;
        Status = "Загрузка карты…";
        try
        {
            var document = await Task.Run(() => mapViewService.LoadAsync(MapName, cancellationToken), cancellationToken);
            Layers = document.Layers;
            SelectedLayer = document.DefaultLayer;
            Document = document;
            Status = string.Empty;
            ApplyFocus();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Загрузка карты остановлена.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsLoading = false;
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
            Status = $"Координаты ({location.X}, {location.Y}, {location.Z}) находятся вне карты станции.";
            return;
        }
        SelectedLayer = layer;
        Status = string.Empty;
    }
}
