@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%CodeWF.EventBus.Socket.slnx"
set "PACKAGE_PROJECT=%ROOT%src\CodeWF.EventBus.Socket\CodeWF.EventBus.Socket.csproj"
set "CONFIGURATION=Release"
set "PACKAGE_DIR=%ROOT%artifacts\packages"

echo [CodeWF.EventBus.Socket] Restore packages...
dotnet restore "%SOLUTION%"
if errorlevel 1 goto :failed

if not exist "%PACKAGE_DIR%" mkdir "%PACKAGE_DIR%"
del /q "%PACKAGE_DIR%\*.nupkg" 2>nul
del /q "%PACKAGE_DIR%\*.snupkg" 2>nul

echo [CodeWF.EventBus.Socket] Build %CONFIGURATION% solution...
dotnet build "%SOLUTION%" -c "%CONFIGURATION%" --no-restore
if errorlevel 1 goto :failed

echo [CodeWF.EventBus.Socket] Pack NuGet package...
dotnet pack "%PACKAGE_PROJECT%" -c "%CONFIGURATION%" --no-build -o "%PACKAGE_DIR%"
if errorlevel 1 goto :failed

if not exist "%PACKAGE_DIR%\*.nupkg" goto :failed

echo.
echo [CodeWF.EventBus.Socket] Packages:
dir /b "%PACKAGE_DIR%\*.nupkg"
echo.
echo [CodeWF.EventBus.Socket] Done. Output: %PACKAGE_DIR%
exit /b 0

:failed
echo.
echo [CodeWF.EventBus.Socket] Pack failed.
exit /b 1
