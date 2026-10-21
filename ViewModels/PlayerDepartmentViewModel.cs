using System.Collections.Generic;
using Avalonia.Media;

namespace Loggepector.ViewModels;

public sealed class PlayerDepartmentViewModel(string name, IBrush accent,
    IReadOnlyList<PlayerInfoViewModel> players) : ViewModelBase
{
    public string Name { get; } = name;
    public IBrush Accent { get; } = accent;
    public IReadOnlyList<PlayerInfoViewModel> Players { get; } = players;
}
