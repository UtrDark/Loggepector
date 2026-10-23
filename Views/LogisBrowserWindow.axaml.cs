using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class LogisBrowserWindow : Window
{
    public LogisBrowserWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            var model = (LogisBrowserViewModel)DataContext!;
            model.PropertyChanged += ModelChanged;
            await model.InitializeAsync();
            if (model.SelectedLog is { } selected)
                RoundsList.ScrollIntoView(selected);
        };
        Closed += (_, _) =>
        {
            var model = (LogisBrowserViewModel)DataContext!;
            model.PropertyChanged -= ModelChanged;
            model.CancelRequests();
        };
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        var model = (LogisBrowserViewModel)DataContext!;
        if (args.PropertyName == nameof(LogisBrowserViewModel.SelectedLog) && model.SelectedLog is { } selected)
            Dispatcher.UIThread.Post(() => RoundsList.ScrollIntoView(selected));
        if (args.PropertyName == nameof(LogisBrowserViewModel.DownloadedPath) && model.DownloadedPath is not null)
            Close(true);
        else if (args.PropertyName == nameof(LogisBrowserViewModel.RequiresLogin) && model.RequiresLogin)
            Close(false);
    }
}
