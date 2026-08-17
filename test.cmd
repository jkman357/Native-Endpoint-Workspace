@echo off
setlocal
cd /d "%~dp0"
call build.cmd
if errorlevel 1 exit /b %ERRORLEVEL%

set "TEST_EXE=NativeEndpointWorkspace.Tests\bin\Release\NativeEndpointWorkspace.Tests.exe"
if not exist "%TEST_EXE%" (
  echo ERROR: Test executable not found: %TEST_EXE%
  exit /b 1
)

"%TEST_EXE%"
if errorlevel 1 exit /b %ERRORLEVEL%

echo.
echo Test PASS
exit /b 0
