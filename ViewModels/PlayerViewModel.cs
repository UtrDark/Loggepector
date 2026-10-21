using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Loggepector.ViewModels;

public partial class PlayerViewModel : ViewModelBase
{
    private readonly Action _selectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public string CKey { get; }

    public PlayerViewModel(string ckey, Action selectionChanged)
    {
        CKey = ckey;
        _selectionChanged = selectionChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
