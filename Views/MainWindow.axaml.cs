using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleSearchKeys, RoutingStrategies.Tunnel);
    }

    private void HandleSearchKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            OpenSearch(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ((MainWindowViewModel)DataContext!).IsSearchVisible)
        {
            CloseSearch(sender, e);
            e.Handled = true;
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
        viewModel.SearchText = string.Empty;
        viewModel.IsSearchVisible = false;
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
