@echo off
setlocal
cd /d "%~dp0"

set "MSBUILD_EXE="
where msbuild >nul 2>nul
if %ERRORLEVEL% EQU 0 set "MSBUILD_EXE=msbuild"

if not defined MSBUILD_EXE if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
  for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD_EXE=%%i"
)

if not defined MSBUILD_EXE (
  echo ERROR: MSBuild was not found.
  echo Open a Visual Studio Developer Command Prompt or install Visual Studio/MSBuild.
  exit /b 1
)

echo Using MSBuild: %MSBUILD_EXE%
"%MSBUILD_EXE%" Native-Endpoint-Workspace.sln /m /t:Rebuild /p:Configuration=Release /v:minimal
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo Build PASS
exit /b 0
