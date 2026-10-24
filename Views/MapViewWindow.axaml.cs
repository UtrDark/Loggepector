using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class MapViewWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public MapViewWindow()
    {
        InitializeComponent();
        Viewport.LoadFailed += message => ((MapViewModel)DataContext!).Status = message;
        Viewport.MarkerClicked += point => ((MapViewModel)DataContext!).ShowPoint(point);
        Opened += LoadMap;
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            var model = (MapViewModel)DataContext!;
            model.EnableMapToolsCommand.Cancel();
            model.RemoveMapToolsCommand.Cancel();
            if (model.Timeline.IsMapToolsBusy)
                model.Timeline.CancelMapToolsCommand.Execute(null);
        };
    }


    private async void LoadMap(object? sender, EventArgs e)
    {
        Opened -= LoadMap;
        var model = (MapViewModel)DataContext!;
        model.ReleaseMapResourcesAsync = Viewport.ClearAsync;
        await model.LoadAsync(_lifetime.Token);
    }

}
