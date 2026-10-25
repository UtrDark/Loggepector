using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using Loggepector.ViewModels;
using Loggepector.Models;

namespace Loggepector.Views;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _mapPreparationCancellation = new();
    private MapViewWindow? _mapWindow;
    private bool _isCopyingLogs;
    private NativeWebDialog? _roundEndBrowser;
    private readonly List<PlayersInfoWindow> _playerWindows = new();

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleSearchKeys, RoutingStrategies.Tunnel);
        Opened += InitializeFind;
        Opened += InitializeMapTools;
        Opened += CheckForUpdates;
        Closed += (_, _) =>
        {
            _roundEndBrowser?.Close();
            _mapWindow?.Close();
            foreach (var window in _playerWindows.ToArray())
                window.Close();
            _mapPreparationCancellation.Cancel();
            var viewModel = (MainWindowViewModel)DataContext!;
            viewModel.EnableMapToolsCommand.Cancel();
            viewModel.CancelLoadCommand.Execute(null);
        };
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        Opened -= CheckForUpdates;
        await ((MainWindowViewModel)DataContext!).CheckForUpdateAsync(_mapPreparationCancellation.Token);
    }

    private async void OpenUpdateRelease(object? sender, RoutedEventArgs e)
    {
        var model = (MainWindowViewModel)DataContext!;
        try
        {
            var address = model.AvailableUpdate ?? throw new InvalidOperationException("Не задан адрес релиза.");
            if (!await Launcher.LaunchUriAsync(address))
                model.ErrorMessage = "Не удалось открыть страницу обновления в браузере.";
        }
        catch (Exception exception) { model.ErrorMessage = exception.Message; }
    }

    private void InitializeFind(object? sender, EventArgs e)
    {
        Opened -= InitializeFind;
        var model = (MainWindowViewModel)DataContext!;
        void UpdateFindPosition(object? source, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentFindResult) && model.CurrentFindResult is { } log)
                LogsList.ScrollIntoView(log);
        }
        model.PropertyChanged += UpdateFindPosition;
        Closed += (_, _) => model.PropertyChanged -= UpdateFindPosition;
    }

    private async void InitializeMapTools(object? sender, EventArgs e)
    {
        Opened -= InitializeMapTools;
        var model = (MainWindowViewModel)DataContext!;
        model.ConfirmMapDownloadAsync = ConfirmMapDownloadAsync;
        await model.InitializeMapToolsAsync(_mapPreparationCancellation.Token);
    }

    private async Task<bool> ConfirmMapDownloadAsync(MapDownloadStage stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var confirmation = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new MapDownloadWindow { DataContext = new MapDownloadViewModel(stage) };
            var owner = _mapWindow is { IsVisible: true } mapWindow ? (Window)mapWindow : this;
            var result = window.ShowDialog<bool>(owner);
            var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => window.Close(false)));
            window.Closed += (_, _) => cancellation.Dispose();
            return result;
        });
        return confirmation;
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
                .Single(control => ReferenceEquals(control.DataContext, entry) && control.Name == "ExpandDetailsButton").Focus();
        }

        e.Handled = true;
    }

    private async void HandleSearchKeys(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.C when e.KeyModifiers == KeyModifiers.Control && CanCopyLogs(e):
                e.Handled = true;
                await CopySelectedLogsAsync();
                break;
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                ((MainWindowViewModel)DataContext!).IsFindVisible = true;
                Dispatcher.UIThread.Post(() =>
                {
                    FindBox.Focus();
                    FindBox.SelectAll();
                });
                e.Handled = true;
                break;
            case Key.Enter when FindBox.IsFocused:
            case Key.F3 when ((MainWindowViewModel)DataContext!).IsFindVisible:
                var model = (MainWindowViewModel)DataContext!;
                var command = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? model.PreviousFindCommand : model.NextFindCommand;
                if (command.CanExecute(null))
                    command.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape when ((MainWindowViewModel)DataContext!).IsFindVisible:
                ((MainWindowViewModel)DataContext!).IsFindVisible = false;
                LogsList.Focus();
                e.Handled = true;
                break;
        }
    }

    private bool CanCopyLogs(KeyEventArgs e)
    {
        if (LogsList.SelectedItems is not { Count: > 0 } || e.Source is not Visual source)
            return false;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        return ancestors.Contains(LogsList) && !ancestors.OfType<TextBox>().Any() &&
               !ancestors.OfType<SelectableTextBlock>().Any(text => text.SelectionStart != text.SelectionEnd);
    }

    private async Task CopySelectedLogsAsync()
    {
        if (_isCopyingLogs)
            return;
        var viewModel = (MainWindowViewModel)DataContext!;
        _isCopyingLogs = true;
        viewModel.ErrorMessage = null;
        try
        {
            var clipboard = Clipboard ?? throw new InvalidOperationException("Буфер обмена недоступен.");
            var selected = LogsList.SelectedItems!.Cast<LogEntryViewModel>()
                .OrderBy(log => log.Entry.Timestamp).ThenBy(log => log.Entry.LineNumber).ToArray();
            using var bitmap = LogImageRenderer.Render(selected, ActualThemeVariant == ThemeVariant.Dark, viewModel.Sanitize);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            var item = new DataTransferItem();
            item.SetBitmap(bitmap);
            item.Set(DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "PNG" : "image/png"), stream.ToArray());
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
            await clipboard.FlushAsync();
            _ = viewModel.ShowCopyNoticeAsync();
        }
        catch (Exception exception)
        {
            viewModel.ErrorMessage = $"Не удалось скопировать PNG: {exception.Message}";
        }
        finally
        {
            _isCopyingLogs = false;
        }
    }

    private void OpenSearch(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        viewModel.IsSearchVisible = !viewModel.IsSearchVisible;
        if (!viewModel.IsSearchVisible)
            return;
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

    private void OpenMapView(object? sender, RoutedEventArgs e) => GetMapWindow().Activate();

    private MapViewWindow GetMapWindow()
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        if (_mapWindow is { } existing)
            return existing;
        _mapWindow?.Close();
        var mapModel = viewModel.CreateMapView();
        var window = new MapViewWindow { DataContext = mapModel };
        async void UpdateMapLogs(object? source, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainWindowViewModel.Logs))
                await mapModel.UpdateLogsAsync(viewModel.Logs.Select(log => log.Entry).ToArray());
            if (args.PropertyName is nameof(MainWindowViewModel.Relative) or nameof(MainWindowViewModel.ShowArea))
                mapModel.UpdateTimeDisplay();
        }
        viewModel.PropertyChanged += UpdateMapLogs;
        _ = mapModel.UpdateLogsAsync(viewModel.Logs.Select(log => log.Entry).ToArray());
        _mapWindow = window;
        window.Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= UpdateMapLogs;
            if (ReferenceEquals(_mapWindow, window))
                _mapWindow = null;
        };
        window.Show();
        return window;
    }

    private async void ShowLogOnMap(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        var entry = (LogEntryViewModel)((Button)sender!).DataContext!;
        try
        {
            if (!viewModel.AreMapsReady)
            {
                viewModel.ErrorMessage = "Сначала скачайте инструменты карт: откройте MapView и нажмите «Включить и скачать».";
                return;
            }
            var mapName = await viewModel.GetLogMapNameAsync(entry.Entry, _mapPreparationCancellation.Token);
            if (!viewModel.CanOpenMap || string.IsNullOrWhiteSpace(mapName))
            {
                viewModel.ErrorMessage = "В логе не найдена карта станции или ещё идёт загрузка.";
                return;
            }
            var coordinates = entry.Entry.GetCoordinates()
                ?? throw new InvalidOperationException("У записи отсутствуют координаты.");
            var window = GetMapWindow();
            var mapModel = (MapViewModel)window.DataContext!;
            mapModel.MapName = mapName;
            mapModel.FocusLocation = new MapLocation(coordinates.X, coordinates.Y, coordinates.Z);
            window.Activate();
            e.Handled = true;
        }
        catch (Exception exception)
        {
            viewModel.ErrorMessage = $"Не удалось открыть запись на карте: {exception.Message}";
        }
    }

    private void OpenPlayersInfo(object? sender, RoutedEventArgs e)
    {
        var viewModel = (MainWindowViewModel)DataContext!;
        var window = new PlayersInfoWindow { DataContext = viewModel.CreatePlayersInfo() };
        _playerWindows.Add(window);
        window.Closed += (_, _) => _playerWindows.Remove(window);
        window.Show();
    }

    private async void ConnectLogis(object? sender, RoutedEventArgs e)
    {
        var model = (MainWindowViewModel)DataContext!;
        var loginAttempted = false;
        try
        {
            model.RestoreLogisSession();
            var requiresLogin = !model.HasLogisSession;
            while (true)
            {
                if (requiresLogin)
                {
                    var login = new LogisWindow { DataContext = model.CreateLogisConnection() };
                    if (!await login.ShowDialog<bool>(this))
                        return;
                    model.RefreshLogisSession();
                    loginAttempted = true;
                }
                var browserModel = model.CreateLogisBrowser(!loginAttempted);
                var browser = new LogisBrowserWindow { DataContext = browserModel };
                await browser.ShowDialog<bool>(this);
                if (browserModel.RequiresLogin)
                {
                    requiresLogin = true;
                    continue;
                }
                if (browserModel.DownloadedPath is not { } path)
                    return;
                try
                {
                    await model.LoadAsync(path);
                    if (model.ErrorMessage is null)
                        model.FileName = browserModel.LogLabel;
                }
                finally
                {
                    File.Delete(path);
                }
                return;
            }
        }
        catch (Exception exception)
        {
            model.ErrorMessage = $"Не удалось открыть лог из Logis: {exception.Message}";
        }
    }

    private void RefreshResources(object? sender, RoutedEventArgs e) => ((MainWindowViewModel)DataContext!).RefreshResources();

    private async void DeleteMapResources(object? sender, RoutedEventArgs e)
    {
        var model = (MainWindowViewModel)DataContext!;
        try
        {
            if (_mapWindow?.DataContext is MapViewModel map)
                await map.RemoveMapToolsCommand.ExecuteAsync(null);
            else
                await model.RemoveMapToolsAsync(_mapPreparationCancellation.Token);
            model.RefreshResources();
        }
        catch (Exception exception) { model.ErrorMessage = exception.Message; }
    }

    private async void OpenRoundEnd(object? sender, RoutedEventArgs e)
    {
        var model = (MainWindowViewModel)DataContext!;
        try
        {
            var html = model.RoundEndHtml ?? throw new InvalidOperationException("В загруженном логе нет roundend.");
            var css = await model.LoadRoundEndStyleAsync(_mapPreparationCancellation.Token);
            if (css is null)
                return;
            html = "<!DOCTYPE html><html><head><meta charset='utf-8'><style>" + css +
                "</style></head><body>" + html + "</body></html>";
            _roundEndBrowser?.Close();
            var browser = new NativeWebDialog { Title = "Roundend — " + model.FileName, CanUserResize = true };
            _roundEndBrowser = browser;
            browser.Resize(1100, 800);
            browser.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    model.ErrorMessage = "Не удалось отобразить roundend в WebView.";
            };
            browser.Closing += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_roundEndBrowser, browser))
                    _roundEndBrowser = null;
                browser.Dispose();
            });
            browser.Show();
            browser.NavigateToString(html, new Uri("about:blank"));
        }
        catch (Exception exception)
        {
            _roundEndBrowser?.Close();
            model.ErrorMessage = $"Не удалось открыть roundend: {exception.Message}";
        }
    }

    private async void OpenBigLogFolder(object? sender, RoutedEventArgs e)
    {
        BigLogButton.Flyout?.Hide();
        var model = (MainWindowViewModel)DataContext!;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выберите папку раунда", AllowMultiple = false
            });
            if (folders.Count == 0)
                return;
            using var folder = folders[0];
            var path = folder.TryGetLocalPath() ?? throw new NotSupportedException("Выберите локальную папку.");
            await model.LoadAsync(path, bigLog: true);
        }
        catch (Exception exception)
        {
            model.ErrorMessage = exception.Message;
        }
    }

    private async void OpenBigLogArchive(object? sender, RoutedEventArgs e)
    {
        BigLogButton.Flyout?.Hide();
        var model = (MainWindowViewModel)DataContext!;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите архив раунда", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Архив 7-Zip") { Patterns = new[] { "*.7z" } } }
            });
            if (files.Count == 0)
                return;
            using var file = files[0];
            var path = file.TryGetLocalPath() ?? throw new NotSupportedException("Выберите локальный архив.");
            await model.LoadAsync(path, bigLog: true);
        }
        catch (Exception exception)
        {
            model.ErrorMessage = exception.Message;
        }
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
