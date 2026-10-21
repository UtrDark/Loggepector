using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _mapPreparationCancellation = new();

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleSearchKeys, RoutingStrategies.Tunnel);
        Opened += InitializeMapTools;
        Closed += (_, _) =>
        {
            _mapPreparationCancellation.Cancel();
            var viewModel = (MainWindowViewModel)DataContext!;
            viewModel.EnableMapToolsCommand.Cancel();
            viewModel.DisableMapToolsCommand.Cancel();
        };
    }

    private async void InitializeMapTools(object? sender, EventArgs e)
    {
        Opened -= InitializeMapTools;
        await ((MainWindowViewModel)DataContext!).InitializeMapToolsAsync(_mapPreparationCancellation.Token);
    }

    private void ToggleLogDetails(object? sender, RoutedEventArgs e)
    {
        var button = (Button)sender!;
        var entry = (LogEntryViewModel)button.DataContext!;
        var container = button.GetVisualAncestors().OfType<ListBoxItem>().First();
        var scroller = container.GetVisualAncestors().OfType<ScrollViewer>().First();
        var position = container.TranslatePoint(default, scroller)!.Value;
        var index = LogsList.IndexFromContainer(container);
        var restoreFocus = button.IsFocused;

        if (restoreFocus)
            LogsList.Focus();

        entry.IsExpanded = !entry.IsExpanded;
        LogsList.UpdateLayout();
        LogsList.ScrollIntoView(index);
        LogsList.UpdateLayout();

        var currentContainer = LogsList.ContainerFromIndex(index);
        var currentPosition = currentContainer.TranslatePoint(default, scroller)!.Value;
        scroller.Offset = new Vector(scroller.Offset.X, scroller.Offset.Y + currentPosition.Y - position.Y);
        LogsList.UpdateLayout();

        if (restoreFocus)
        {
            var focusedContainer = LogsList.ContainerFromIndex(index);
            focusedContainer.GetVisualDescendants().OfType<Button>()
                .Single(control => ReferenceEquals(control.DataContext, entry)).Focus();
        }

        e.Handled = true;
    }

    private void HandleSearchKeys(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                OpenSearch(sender, e);
                e.Handled = true;
                break;
            case Key.Escape when ((MainWindowViewModel)DataContext!).IsSearchVisible:
                CloseSearch(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void OpenSearch(object? sender, RoutedEventArgs e)
    {
        ((MainWindowViewModel)DataContext!).IsSearchVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
    }

    private void CloseSearch(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        viewModel.IsSearchVisible = false;
    }

    private void OpenMapView(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        var window = new MapViewWindow { DataContext = viewModel.CreateMapView() };
        window.Show(this);
    }

    private void OpenPlayersInfo(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        var window = new PlayersInfoWindow { DataContext = viewModel.CreatePlayersInfo() };
        window.Show(this);
    }

    private async void OpenLogFile(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите файл логов",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Файлы логов") { Patterns = new[] { "*.log" } }
                }
            });

            if (files.Count == 0)
                return;

            using var file = files[0];
            var path = file.TryGetLocalPath()
                ?? throw new NotSupportedException("Выберите локальный файл логов.");
            await viewModel.LoadAsync(path);
        }
        catch (Exception exception)
        {
            viewModel.ErrorMessage = exception.Message;
        }
    }
}
