using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Loggepector.ViewModels;
using Loggepector.Views;

namespace Loggepector;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        null => null,
        MainWindowViewModel => new MainWindow(),
        MapViewModel => new MapViewWindow(),
        PlayersInfoViewModel => new PlayersInfoWindow(),
        _ => throw new InvalidOperationException($"Не настроено представление для {param.GetType().Name}.")
    };

    public bool Match(object? data) => data is MainWindowViewModel or MapViewModel or PlayersInfoViewModel;
}
