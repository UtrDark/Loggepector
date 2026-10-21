using System;
using System.Threading;
using Avalonia.Controls;
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
            ((MapViewModel)DataContext!).EnableMapToolsCommand.Cancel();
        };
    }

    private async void LoadMap(object? sender, EventArgs e)
    {
        Opened -= LoadMap;
        await ((MapViewModel)DataContext!).LoadAsync(_lifetime.Token);
    }

}
