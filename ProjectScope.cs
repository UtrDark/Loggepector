using Autofac;
using Loggepector.ViewModels;
using Loggepector.Services;

namespace Loggepector;

public static class ProjectScope
{
    public static IContainer Build()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<PlayerJoinService>().As<IPlayerJoinService>().SingleInstance();
        builder.RegisterType<LogService>().As<ILogService>().SingleInstance();
        builder.RegisterType<MainWindowViewModel>().SingleInstance();
        return builder.Build();
    }
}
