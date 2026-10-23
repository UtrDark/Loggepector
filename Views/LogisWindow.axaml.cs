using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Loggepector.ViewModels;

namespace Loggepector.Views;

public partial class LogisWindow : Window
{
    private NativeWebDialog? _browser;
    private bool _readingCookies;
    private bool _closed;

    public LogisWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ((LogisViewModel)DataContext!).PropertyChanged += ModelChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            ((LogisViewModel)DataContext!).PropertyChanged -= ModelChanged;
            var browser = _browser;
            _browser = null;
            if (browser is not null)
            {
                browser.Close();
                Dispatcher.UIThread.Post(browser.Dispose);
            }
        };
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        var model = (LogisViewModel)DataContext!;
        if (args.PropertyName == nameof(LogisViewModel.IsSessionCaptured) && model.IsSessionCaptured)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed)
                    Close(true);
            });
            return;
        }
        if (args.PropertyName != nameof(LogisViewModel.IsLoginOpen) || !model.IsLoginOpen)
            return;
        OpenNativeLogin(this, new RoutedEventArgs());
    }

    private void OpenNativeLogin(object? sender, RoutedEventArgs args)
    {
        if (_browser is not null)
            return;
        var model = (LogisViewModel)DataContext!;
        try
        {
            model.ErrorMessage = null;
            var browser = new NativeWebDialog
            {
                Title = "Вход в Logis",
                CanUserResize = true,
                Source = model.LoginAddress!
            };
            _browser = browser;
            browser.Resize(1000, 760);
            browser.EnvironmentRequested += (_, e) =>
            {
                if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                {
                    windows.ProfileName = "Logis";
                    windows.IsInPrivateModeEnabled = true;
                }
                if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
                    gtk.EphemeralDataManager = true;
            };
            browser.NavigationStarted += (_, e) =>
            {
                model.ObserveNavigation(e.Request);
                if (e.Request is { Scheme: "discord" } address)
                {
                    e.Cancel = true;
                    Dispatcher.UIThread.Post(async () => await OpenDiscordAsync(address));
                }
            };
            browser.NavigationCompleted += NavigationCompleted;
            browser.NewWindowRequested += (_, e) =>
            {
                if (e.Request is { Scheme: "discord" } discordAddress)
                {
                    e.Handled = true;
                    Dispatcher.UIThread.Post(async () => await OpenDiscordAsync(discordAddress));
                    return;
                }
                if (e.Request is { Scheme: "https" } address)
                {
                    e.Handled = true;
                    browser.Navigate(address);
                }
            };
            browser.Closing += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(_browser, browser))
                    return;
                _browser = null;
                model.IsBrowserOpen = false;
                browser.Dispose();
            });
            model.IsBrowserOpen = true;
            browser.Show();
        }
        catch (Exception exception)
        {
            _browser?.Dispose();
            _browser = null;
            model.IsBrowserOpen = false;
            model.ErrorMessage = $"Не удалось открыть нативное окно входа ({exception.GetType().Name}). На Windows требуется WebView2 Runtime, на Linux — WebKitGTK.";
        }
    }

    private async Task OpenDiscordAsync(Uri address)
    {
        if (_closed)
            return;
        var model = (LogisViewModel)DataContext!;
        try
        {
            if (!await Launcher.LaunchUriAsync(address))
                model.ErrorMessage = "Не удалось открыть Discord. Проверьте, что приложение установлено и обрабатывает ссылки discord://.";
        }
        catch (Exception)
        {
            if (!_closed)
                model.ErrorMessage = "Не удалось передать ссылку приложению Discord. Можно продолжить вход в окне WebView.";
        }
    }

    private async void NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (_closed || !ReferenceEquals(sender, _browser))
            return;
        var model = (LogisViewModel)DataContext!;
        if (!args.IsSuccess)
        {
            model.ErrorMessage = "Страница входа не загрузилась. Проверьте доступность сервера и подключение.";
            return;
        }
        model.ErrorMessage = null;
        if (args.Request?.GetLeftPart(UriPartial.Authority) == model.LoginAddress?.GetLeftPart(UriPartial.Authority))
            await ReadCookiesAsync();
    }

    private async Task ReadCookiesAsync()
    {
        if (_readingCookies || _closed)
            return;
        _readingCookies = true;
        var browser = _browser;
        var model = (LogisViewModel)DataContext!;
        try
        {
            var manager = browser?.TryGetCookieManager()
                ?? throw new NotSupportedException("WebView не предоставляет доступ к cookie. Проверьте установленный браузерный runtime.");
            var cookies = await manager.GetCookiesAsync();
            if (!_closed && ReferenceEquals(browser, _browser))
            {
                model.ErrorMessage = null;
                model.UpdateCookies(cookies);
            }
        }
        catch (Exception)
        {
            if (!_closed)
                model.ErrorMessage = "Не удалось прочитать cookie Logis. Проверьте поддержку cookie браузерным runtime и завершите вход.";
        }
        finally
        {
            _readingCookies = false;
        }
    }
}
