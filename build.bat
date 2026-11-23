@echo off
chcp 65001 >nul
cd /d "%~dp0Source\DigitalStorageUnit"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet build -c Release -nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED - see the error lines above.
  pause
  exit /b 1
)
echo.
echo Done: 1.7\Assemblies\DigitalStorageUnit.dll has been built.
pause
