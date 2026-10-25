using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Loggepector.Services;

namespace Loggepector.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IUpdateService _updateService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private Uri? _availableUpdate;

    [ObservableProperty] private string _updateCheckStatus = string.Empty;
    [ObservableProperty] private bool _updateCheckFailed;
    public bool HasUpdate => AvailableUpdate is not null;
    public IBrush UpdateNoticeColor => Brush.Parse(IsDarkTheme ? "#E6C351" : "#806000");

    public async Task CheckForUpdateAsync(Func<CancellationToken, Task<bool?>> requestConsent, CancellationToken cancellationToken)
    {
        try
        {
            var update = await _updateService.CheckAsync(requestConsent, cancellationToken);
            AvailableUpdate = update.Url;
            UpdateCheckStatus = update.Description;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            UpdateCheckFailed = true;
            UpdateCheckStatus = $"Не удалось проверить обновления: {exception.Message}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Ошибка сведений о сборке: {exception.Message}";
        }
    }
}
