using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;

namespace Loggepector.ViewModels;

public partial class LogTypeViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterMark))]
    [NotifyPropertyChangedFor(nameof(FilterColor))]
    [NotifyPropertyChangedFor(nameof(FilterDescription))]
    private bool? _isSelected = false;

    public string FilterMark => IsSelected switch { true => "✓", null => "✕", false => string.Empty };
    public IBrush FilterColor => IsSelected switch { true => Brushes.LimeGreen, null => Brushes.IndianRed, false => Brushes.Gray };
    public string FilterDescription => IsSelected switch
    {
        true => "Фильтровать по типу",
        null => "Исключать из итогового списка",
        false => "Не фильтровать по типу"
    };

    [RelayCommand]
    private void CycleFilter() => IsSelected = IsSelected switch { false => true, true => null, null => false };

    public string Type { get; }
    public string Label => Type.Length == 0 ? "Без типа" : Type;

    public LogTypeViewModel(string type, Action selectionChanged)
    {
        Type = type;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool? value) => _selectionChanged();
}
