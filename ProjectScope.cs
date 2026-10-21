using Autofac;
using Loggepector.ViewModels;
using Loggepector.Services;

namespace Loggepector;

public static class ProjectScope
{
    public static IContainer Build()
    {
        var builder = new ContainerBuilder();
        builder.Register(_ => new ThemeService()).As<IThemeService>().SingleInstance();
        builder.Register(_ => new PlayerJoinService()).As<IPlayerJoinService>().SingleInstance();
        builder.Register(scope => new LogService(scope.Resolve<IPlayerJoinService>())).As<ILogService>().SingleInstance();
        builder.Register(_ => new MapPreparationService()).As<IMapPreparationService>().SingleInstance();
        builder.Register(_ => new MapViewService()).As<IMapViewService>().SingleInstance();
        builder.Register(scope => new MainWindowViewModel(scope.Resolve<ILogService>(),
            scope.Resolve<IMapPreparationService>(), scope.Resolve<IMapViewService>(), scope.Resolve<IThemeService>())).SingleInstance();
        return builder.Build();
    }
}
