using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;

namespace Loggepector.Services;

public sealed class ThemeService : IThemeService
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "theme-settings.json");

    public bool IsDark { get; private set; }

    public void Load()
    {
        var theme = File.Exists(_path) ? JsonSerializer.Deserialize<string>(File.ReadAllText(_path)) : "Light";
        IsDark = theme switch
        {
            "Light" => false,
            "Dark" => true,
            _ => throw new InvalidDataException("Неизвестная тема в theme-settings.json.")
        };
        Apply();
    }

    public void SetTheme(bool isDark)
    {
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(isDark ? "Dark" : "Light"));
        File.Move(temporary, _path, true);
        IsDark = isDark;
        Apply();
    }

    private void Apply()
    {
        var application = Application.Current ?? throw new InvalidOperationException("Приложение ещё не создано.");
        application.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
