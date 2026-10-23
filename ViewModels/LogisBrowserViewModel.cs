using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loggepector.Models;
using Loggepector.Services;

namespace Loggepector.ViewModels;

public partial class LogisBrowserViewModel : ViewModelBase
{
    private readonly ILogisService _service;
    private bool _allowAutomaticLogin;
    private bool _restoringSelection;
    private CancellationTokenSource? _selectionCancellation;

    [ObservableProperty] private IReadOnlyList<string> _servers = Array.Empty<string>();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstDate))]
    [NotifyPropertyChangedFor(nameof(LastDate))]
    [NotifyPropertyChangedFor(nameof(CalendarDisplayDate))]
    private IReadOnlyList<string> _dates = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<LogisLogFile> _logs = Array.Empty<LogisLogFile>();
    [ObservableProperty] private string? _selectedServer;
    [ObservableProperty] private string? _selectedDate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalendarDisplayDate))]
    private DateTime? _selectedCalendarDate;
    [ObservableProperty] private string _roundSearchText = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private LogisLogFile? _selectedLog;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool _isDownloading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "Выберите сервер.";
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private string? _downloadedPath;
    [ObservableProperty] private bool _requiresLogin;
    [ObservableProperty] private bool _isSessionExpired;

    public bool CanSelect => !IsDownloading;
    public bool HasProgress => IsBusy || IsDownloading;
    public string LogLabel => $"{SelectedServer} · {SelectedDate} · {SelectedLog!.DisplayName}";

    public DateTime? FirstDate => Dates.Count == 0 ? null : ParseDate(Dates[^1]);
    public DateTime? LastDate => Dates.Count == 0 ? null : ParseDate(Dates[0]);
    public DateTime CalendarDisplayDate => SelectedCalendarDate ?? LastDate ?? DateTime.Today;

    public LogisBrowserViewModel(ILogisService service, bool allowAutomaticLogin)
    {
        _service = service;
        _allowAutomaticLogin = allowAutomaticLogin;
    }

    private static DateTime ParseDate(string date) => DateTime.ParseExact(date, "yyyy/MM/dd", CultureInfo.InvariantCulture);

    public void PrepareOpen(bool allowAutomaticLogin)
    {
        _allowAutomaticLogin = allowAutomaticLogin;
        DownloadedPath = null;
        RequiresLogin = false;
        IsSessionExpired = false;
    }

    public async Task InitializeAsync()
    {
        var cancellation = BeginSelection();
        try
        {
            if (DownloadCommand.IsRunning && DownloadCommand.ExecutionTask is { } downloading)
                await downloading;
            var servers = await _service.GetServersAsync(cancellation.Token);
            if (!ReferenceEquals(cancellation, _selectionCancellation))
                return;
            var selectedServer = SelectedServer;
            if (!Servers.SequenceEqual(servers, StringComparer.Ordinal))
            {
                _restoringSelection = true;
                try
                {
                    Servers = servers;
                    SelectedServer = selectedServer;
                }
                finally
                {
                    _restoringSelection = false;
                }
            }
            if (selectedServer is not null && !servers.Contains(selectedServer, StringComparer.Ordinal))
            {
                SelectedServer = null;
                Status = "Ранее выбранный сервер больше недоступен. Выберите сервер.";
            }
            else if (selectedServer is not null && Dates.Count == 0)
                await LoadDatesAsync(selectedServer);
            else if (selectedServer is not null && SelectedDate is { } date && Logs.Count == 0)
                await LoadLogsAsync(selectedServer, date);
            else if (selectedServer is not null)
                Status = SelectedLog is null ? string.Empty : $"Выбран {SelectedLog.DisplayName}.";
            else
                Status = Servers.Count == 0 ? "Нет доступных серверов." : "Выберите сервер.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                ShowError(exception);
        }
        finally
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                IsBusy = false;
        }
    }

    partial void OnSelectedServerChanged(string? value)
    {
        if (_restoringSelection)
            return;
        SelectedCalendarDate = null;
        SelectedDate = null;
        Dates = Array.Empty<string>();
        SelectedLog = null;
        Logs = Array.Empty<LogisLogFile>();
        if (value is not null)
            _ = LoadDatesAsync(value);
    }

    partial void OnSelectedDateChanged(string? value)
    {
        if (_restoringSelection)
            return;
        SelectedLog = null;
        Logs = Array.Empty<LogisLogFile>();
        if (value is not null)
            _ = LoadLogsAsync(SelectedServer!, value);
        else
        {
            _selectionCancellation?.Cancel();
            _selectionCancellation?.Dispose();
            _selectionCancellation = null;
            IsBusy = false;
        }
    }

    partial void OnSelectedCalendarDateChanged(DateTime? value)
    {
        if (_restoringSelection)
            return;
        var date = value?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        if (date is not null && !Dates.Contains(date, StringComparer.Ordinal))
        {
            _selectionCancellation?.Cancel();
            _selectionCancellation?.Dispose();
            _selectionCancellation = null;
            IsBusy = false;
            SelectedDate = null;
            Status = "На выбранный день нет доступных логов.";
            return;
        }
        SelectedDate = date;
    }

    partial void OnRoundSearchTextChanged(string value) => FindRound();

    private void FindRound()
    {
        if (string.IsNullOrWhiteSpace(RoundSearchText))
        {
            Status = string.Empty;
            return;
        }
        if (SelectedDate is null)
        {
            Status = "Сначала выберите сервер и день.";
            return;
        }
        if (!int.TryParse(RoundSearchText.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            Status = "Введите номер раунда.";
            return;
        }
        var found = Logs.FirstOrDefault(log => log.RoundNumber == number);
        SelectedLog = found;
        Status = found is null
            ? "Раунд не найден за выбранный день. Выберите другой день в календаре."
            : $"Найден раунд {number}.";
    }

    private CancellationTokenSource BeginSelection()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = new CancellationTokenSource();
        IsBusy = true;
        ErrorMessage = null;
        return _selectionCancellation;
    }

    private async Task LoadDatesAsync(string server)
    {
        var cancellation = BeginSelection();
        try
        {
            var dates = await _service.GetDatesAsync(server, cancellation.Token);
            if (!ReferenceEquals(cancellation, _selectionCancellation))
                return;
            Dates = dates;
            Status = Dates.Count == 0 ? "У сервера нет доступных дней." : "Выберите день.";
            SelectedCalendarDate = LastDate;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                ShowError(exception);
        }
        finally
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                IsBusy = false;
        }
    }

    private async Task LoadLogsAsync(string server, string date)
    {
        var cancellation = BeginSelection();
        try
        {
            var logs = await _service.GetLogsAsync(server, date, cancellation.Token);
            if (!ReferenceEquals(cancellation, _selectionCancellation))
                return;
            Logs = logs;
            Status = Logs.Count == 0 ? "За этот день нет game.log." : "Выберите лог.";
            FindRound();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                ShowError(exception);
        }
        finally
        {
            if (ReferenceEquals(cancellation, _selectionCancellation))
                IsBusy = false;
        }
    }

    private bool CanDownload => SelectedLog is not null && !IsBusy && !IsDownloading;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task Download(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"loggepector-{Guid.NewGuid():N}.log");
        IsDownloading = true;
        ErrorMessage = null;
        DownloadProgress = 0;
        Status = "Загрузка game.log…";
        try
        {
            var progress = new Progress<int>(value => DownloadProgress = value);
            await _service.DownloadGameLogAsync(SelectedServer!, SelectedDate!, SelectedLog!, path, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            DownloadedPath = path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Загрузка отменена.";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            IsDownloading = false;
            if (DownloadedPath != path && File.Exists(path))
                File.Delete(path);
        }
    }

    private void ShowError(Exception exception)
    {
        ErrorMessage = exception.Message;
        IsSessionExpired = exception is UnauthorizedAccessException;
        if (IsSessionExpired && _allowAutomaticLogin)
            RequiresLogin = true;
    }

    [RelayCommand]
    private void Reauthenticate() => RequiresLogin = true;

    public void CancelRequests()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        IsBusy = false;
        DownloadCommand.Cancel();
    }
}
