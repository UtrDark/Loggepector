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
    public string MapName { get; } = mapName;

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
}
