using System;
using System.Threading;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class MapViewWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public MapViewWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleFindKeys, RoutingStrategies.Tunnel);
        Opened += (_, _) => ((MapViewModel)DataContext!).PropertyChanged += FindResultChanged;
        Viewport.LoadFailed += message => ((MapViewModel)DataContext!).Status = message;
        Viewport.MarkerClicked += point => ((MapViewModel)DataContext!).ShowPoint(point);
        Opened += LoadMap;
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            var model = (MapViewModel)DataContext!;
            model.PropertyChanged -= FindResultChanged;
            model.ClosePointFindCommand.Execute(null);
            model.EnableMapToolsCommand.Cancel();
            model.RemoveMapToolsCommand.Cancel();
            if (model.Timeline.IsMapToolsBusy)
                model.Timeline.CancelMapToolsCommand.Execute(null);
        };
    }

    private void FindResultChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.CurrentPointFindResult) &&
            ((MapViewModel)DataContext!).CurrentPointFindResult is { } entry)
            PointLogsList.ScrollIntoView(entry);
    }

    private void HandleFindKeys(object? sender, KeyEventArgs e)
    {
        var model = (MapViewModel)DataContext!;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && model.HasSelectedPoint)
        {
            model.IsPointFindVisible = true;
            Dispatcher.UIThread.Post(() => { PointFindBox.Focus(); PointFindBox.SelectAll(); });
            e.Handled = true;
        }
        else if ((e.Key == Key.Enter && PointFindBox.IsFocused) || (e.Key == Key.F3 && model.IsPointFindVisible))
        {
            var command = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? model.PreviousPointFindCommand : model.NextPointFindCommand;
            if (command.CanExecute(null)) command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && model.IsPointFindVisible)
        {
            model.ClosePointFindCommand.Execute(null);
            PointLogsList.Focus();
            e.Handled = true;
        }
    }


    private async void LoadMap(object? sender, EventArgs e)
    {
        Opened -= LoadMap;
        var model = (MapViewModel)DataContext!;
        model.ReleaseMapResourcesAsync = Viewport.ClearAsync;
        await model.LoadAsync(_lifetime.Token);
    }

}
