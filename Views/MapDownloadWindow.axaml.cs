using System.ComponentModel;
using Avalonia.Controls;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class MapDownloadWindow : Window
{
    public MapDownloadWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ((MapDownloadViewModel)DataContext!).PropertyChanged += ModelChanged;
        Closed += (_, _) => ((MapDownloadViewModel)DataContext!).PropertyChanged -= ModelChanged;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        var model = (MapDownloadViewModel)DataContext!;
        if (args.PropertyName == nameof(MapDownloadViewModel.Decision) && model.Decision is { } decision)
            Close(decision);
    }
}
