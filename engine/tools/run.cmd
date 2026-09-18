@echo off
setlocal
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\src\PhoneLinkDiag\PhoneLinkDiag.csproj"
where dotnet >nul 2>nul || (
  echo .NET 8 SDK was not found on PATH.
  exit /b 1
)
dotnet run --project "%PROJECT%" -c Release
exit /b %ERRORLEVEL%
