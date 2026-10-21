using System;
using System.Linq;
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
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleSearchKeys, RoutingStrategies.Tunnel);
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
