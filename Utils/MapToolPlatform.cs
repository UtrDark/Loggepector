using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Loggepector.Utils;

internal static class MapToolPlatform
{
    internal static string RendererName => (OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), RuntimeInformation.ProcessArchitecture) switch
    {
        (true, _, Architecture.X64) => "dmm-tools.exe",
        (_, true, Architecture.X64) => "dmm-tools",
        _ => throw new PlatformNotSupportedException("Рендер карт поддерживается на Windows x64 и Linux x64.")
    };

    internal static string GitName => OperatingSystem.IsWindows() ? "git.exe" : "git";

    internal static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) == 0)
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute);
        }
    }
}
