@echo off
chcp 65001 >nul
cd /d "%~dp0Source\DigitalStorageUnit"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK bulunamadi. https://dotnet.microsoft.com/download/dotnet/10.0 adresinden .NET 10 SDK kurun.
  pause
  exit /b 1
)
dotnet build -c Release -nologo
if errorlevel 1 (
  echo.
  echo DERLEME HATASI - yukaridaki hata satirlarini kopyalayip gonderin.
  pause
  exit /b 1
)
echo.
echo Tamam: 1.7\Assemblies\DigitalStorageUnit.dll olusturuldu.
pause
