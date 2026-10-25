using Autofac;
using Loggepector.ViewModels;
using Loggepector.Services;

namespace Loggepector;

public static class ProjectScope
{
    public static IContainer Build()
    {
        var builder = new ContainerBuilder();
        builder.Register(_ => new UpdateService()).As<IUpdateService>().SingleInstance();
        builder.Register(_ => new LogisService()).As<ILogisService>().SingleInstance();
        builder.Register(_ => new EmojiService()).As<IEmojiService>().SingleInstance();
        builder.Register(_ => new RoundEndStyleService()).As<IRoundEndStyleService>().SingleInstance();
        builder.Register(_ => new ThemeService()).As<IThemeService>().SingleInstance();
        builder.Register(_ => new PlayerJoinService()).As<IPlayerJoinService>().SingleInstance();
        builder.Register(_ => new BigLogParser()).As<IBigLogParser>().SingleInstance();
        builder.Register(scope => new BigLogService(scope.Resolve<IBigLogParser>())).As<IBigLogService>().SingleInstance();
        builder.Register(scope => new LogService(scope.Resolve<IPlayerJoinService>(), scope.Resolve<IBigLogService>())).As<ILogService>().SingleInstance();
        builder.Register(_ => new MapPreparationService()).As<IMapPreparationService>().SingleInstance();
        builder.Register(_ => new MapViewService()).As<IMapViewService>().SingleInstance();
        builder.Register(scope => new MainWindowViewModel(scope.Resolve<ILogService>(),
            scope.Resolve<IMapPreparationService>(), scope.Resolve<IMapViewService>(), scope.Resolve<IThemeService>(), scope.Resolve<ILogisService>(), scope.Resolve<IRoundEndStyleService>(), scope.Resolve<IEmojiService>(), scope.Resolve<IUpdateService>())).SingleInstance();
        return builder.Build();
    }
}
