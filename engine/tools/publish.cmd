@echo off
setlocal
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\src\PhoneLinkDiag\PhoneLinkDiag.csproj"
set "OUT=%LOCALAPPDATA%\iPhoneLinkCRM\App"
where dotnet >nul 2>nul || (
  echo .NET 8 SDK was not found on PATH.
  exit /b 1
)
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%OUT%" (
  echo Could not clean previous published application at "%OUT%". Close iPhoneLink CRM and retry.
  exit /b 1
)
mkdir "%OUT%" || exit /b 1
dotnet restore "%PROJECT%" || exit /b 1
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 exit /b 1
if exist "%ROOT%\src\PhoneLinkDiag\Assets\shell" (
  if not exist "%OUT%\shell" mkdir "%OUT%\shell"
  xcopy /y /q /i "%ROOT%\src\PhoneLinkDiag\Assets\shell\*" "%OUT%\shell\" >nul
)
exit /b 0
