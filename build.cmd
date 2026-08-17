@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if not exist "logs" mkdir "logs"
set "BUILD_LOG=%CD%\logs\build.log"

set "MSBUILD_EXE="
where msbuild >nul 2>nul
if %ERRORLEVEL% EQU 0 set "MSBUILD_EXE=msbuild"

if not defined MSBUILD_EXE if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" (
  for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD_EXE=%%i"
)

if not defined MSBUILD_EXE (
  > "%BUILD_LOG%" echo Native Endpoint Workspace build failed before MSBuild launch.
  >>"%BUILD_LOG%" echo Date: %DATE% %TIME%
  >>"%BUILD_LOG%" echo ERROR: MSBuild was not found.
  echo ERROR: MSBuild was not found.
  echo Open a Visual Studio Developer Command Prompt or install Visual Studio/MSBuild.
  echo Build log: "%BUILD_LOG%"
  exit /b 1
)

echo Using MSBuild: %MSBUILD_EXE%
echo Build log: "%BUILD_LOG%"

"%MSBUILD_EXE%" Native-Endpoint-Workspace.sln /m /t:Rebuild /p:Configuration=Release /v:minimal /fl /flp:"logfile=%BUILD_LOG%;verbosity=diagnostic;encoding=UTF-8"
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
  echo.
  echo Build FAIL ^(exit code %BUILD_EXIT%^)
  echo Detailed log: "%BUILD_LOG%"
  exit /b %BUILD_EXIT%
)

echo.
echo Build PASS
echo Detailed log: "%BUILD_LOG%"
exit /b 0
