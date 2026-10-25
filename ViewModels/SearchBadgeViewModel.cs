using System.Collections.Generic;
using Loggepector.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Loggepector.ViewModels;

public sealed partial class SearchBadgeViewModel(LogMessagePart part) : ObservableObject
{
    public LogMessagePart Part { get; } = part;
    public IReadOnlyList<LogMessagePart> Parts { get; } = new[] { part };
    [ObservableProperty]
    private bool _isExcluded;
    public LogMessagePart FilterPart => Part with { IsExcluded = IsExcluded };
}
