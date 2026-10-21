@echo off
setlocal
set "profile=NativeAot"
set "output=native-aot"
if "%~1"=="" goto publish
if /I "%~1"=="aot" goto publish
if /I "%~1"=="singlefile" goto singlefile
echo Usage: build.bat [aot^|singlefile]
exit /b 2

:singlefile
set "profile=SingleFile"
set "output=singlefile"

:publish
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install the .NET 8 SDK or newer.
    exit /b 1
)
echo Publishing %profile% for Windows x64...
if /I "%profile%"=="NativeAot" echo Requires Visual Studio C++ Build Tools and Windows SDK.
dotnet publish "%~dp0Loggepector.csproj" -c Release -r win-x64 --self-contained true -p:PublishProfile=%profile%
if errorlevel 1 (
    echo ERROR: Publish failed. See diagnostics above.
    exit /b 1
)
echo Output: "%~dp0artifacts\%output%"
if /I "%profile%"=="NativeAot" echo Keep all native DLLs next to the executable.
exit /b 0
