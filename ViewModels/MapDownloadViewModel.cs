using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loggepector.Models;

namespace Loggepector.ViewModels;

public partial class MapDownloadViewModel(MapDownloadStage stage) : ViewModelBase
{
    public string Title => stage.Title;
    public string Description => stage.Description;
    public IReadOnlyList<MapDownloadFile> Files => stage.Files;
    public string FileCount => $"Файлов: {Files.Count:N0}";

    [ObservableProperty]
    private bool? _decision;

    [RelayCommand]
    private void Approve() => Decision = true;

    [RelayCommand]
    private void Decline() => Decision = false;
}
