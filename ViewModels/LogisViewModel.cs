using System;
using System.Collections.Generic;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loggepector.Services;

namespace Loggepector.ViewModels;

public partial class LogisViewModel : ViewModelBase
{
    private readonly ILogisService _service;
    private bool _discordLoginStarted;

    [ObservableProperty] private string _address;
    [ObservableProperty] private Uri? _loginAddress;
    [ObservableProperty] private bool _isLoginOpen;
    [ObservableProperty] private bool _isBrowserOpen;
    [ObservableProperty] private bool _isSessionCaptured;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _status = "Войдите через Discord. Сессия Logis будет получена автоматически.";

    public LogisViewModel(ILogisService service)
    {
        _service = service;
        _address = service.LoadAddress();
    }

    [RelayCommand]
    private void Continue()
    {
        ErrorMessage = null;
        if (!Uri.TryCreate(Address.Trim(), UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo))
        {
            ErrorMessage = "Введите HTTPS-адрес сервера Logis.";
            return;
        }
        try
        {
            _service.SaveAddress(address);
            LoginAddress = address;
            IsLoginOpen = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Не удалось сохранить адрес Logis: {exception.Message}";
        }
    }

    public void ObserveNavigation(Uri? address)
    {
        if (address is not null && address.Scheme == Uri.UriSchemeHttps &&
            (address.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
             address.Host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)))
            _discordLoginStarted = true;
    }

    public void UpdateCookies(IReadOnlyList<Cookie> cookies)
    {
        var address = LoginAddress ?? throw new InvalidOperationException("Не указан адрес Logis.");
        var container = new CookieContainer();
        foreach (var cookie in cookies)
        {
            var domain = cookie.Domain.TrimStart('.');
            if (string.Equals(domain, address.Host, StringComparison.OrdinalIgnoreCase) ||
                address.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                container.Add(cookie);
        }
        var session = container.GetCookies(address)["PHPSESSID"];
        if (!_discordLoginStarted || session is null || session.Expired || string.IsNullOrEmpty(session.Value))
        {
            Status = "Ожидание входа через Discord и возврата в Logis…";
            return;
        }
        _service.SetSession(address, session);
        IsSessionCaptured = true;
    }
}
