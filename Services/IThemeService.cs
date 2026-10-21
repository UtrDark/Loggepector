namespace Loggepector.Services;

public interface IThemeService
{
    bool IsDark { get; }
    void Load();
    void SetTheme(bool isDark);
}
