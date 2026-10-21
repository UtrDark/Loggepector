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
        Closed += (_, _) => _lifetime.Cancel();
    }

    private async void LoadMap(object? sender, EventArgs e)
    {
        Opened -= LoadMap;
        await ((MapViewModel)DataContext!).LoadAsync(_lifetime.Token);
    }

    private async void ReloadMap(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MapViewModel)DataContext!;
        if (!viewModel.IsLoading)
            await viewModel.LoadAsync(_lifetime.Token);
    }
}
