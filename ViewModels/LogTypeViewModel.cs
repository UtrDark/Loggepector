using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Loggepector.ViewModels;

public partial class LogTypeViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    [ObservableProperty]
    private bool _isSelected = true;

    public string Type { get; }
    public string Label => Type.Length == 0 ? "Без типа" : Type;

    public LogTypeViewModel(string type, Action selectionChanged)
    {
        Type = type;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
