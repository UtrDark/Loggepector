using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loggepector.Services;
using Loggepector.Utils;

namespace Loggepector.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IEmojiService _emojiService;
    [ObservableProperty] private bool _hasMapResources;
    [ObservableProperty] private bool _hasRoundEndResources;
    [ObservableProperty] private bool _hasEmojiResources;
    [ObservableProperty] private bool _isResourcesBusy;

    public void RefreshResources()
    {
        HasMapResources = _mapPreparation.HasLocalData;
        HasRoundEndResources = _roundEndStyle.HasLocalData;
        HasEmojiResources = _emojiService.HasLocalData;
    }

    [RelayCommand]
    private async Task SaveRepository(CancellationToken token)
    {
        try
        {
            await _mapPreparation.SaveChoiceAsync(await _mapPreparation.GetChoiceAsync(token) == true, MapRepositoryUrl, token);
            if (HasLogs)
                await PrepareEmojiAsync(token);
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }

    private async Task PrepareEmojiAsync(CancellationToken token)
    {
        if (!_logService.GetLogs(TimeSpan.Zero, _logService.Duration).Any(entry => EmojiMarkup.Contains(entry.GetRawMessage())))
            return;
        IsResourcesBusy = true;
        try
        {
            await _emojiService.PrepareAsync(MapRepositoryUrl, ConfirmMapDownloadAsync
                ?? throw new InvalidOperationException("Не подключено подтверждение загрузки смайликов."), token);
            foreach (var log in Logs.Concat(SelectedLogs).Distinct()) log.RefreshMessageParts();
        }
        finally
        {
            IsResourcesBusy = false;
            RefreshResources();
        }
    }

    [RelayCommand]
    private void DeleteRoundEndResources()
    {
        try { _roundEndStyle.DeleteAll(); RefreshResources(); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }

    [RelayCommand]
    private void DeleteEmojiResources()
    {
        try
        {
            _emojiService.DeleteAll();
            foreach (var log in Logs.Concat(SelectedLogs).Distinct()) log.RefreshMessageParts();
            RefreshResources();
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }
}
