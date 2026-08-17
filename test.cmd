@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if not exist "logs" mkdir "logs"
set "TEST_LOG=%CD%\logs\test.log"

call build.cmd
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
  > "%TEST_LOG%" echo Test run aborted because build failed.
  >>"%TEST_LOG%" echo See logs\build.log for detailed MSBuild diagnostics.
  echo Test log: "%TEST_LOG%"
  exit /b %BUILD_EXIT%
)

set "TEST_EXE=NativeEndpointWorkspace.Tests\bin\Release\NativeEndpointWorkspace.Tests.exe"
if not exist "%TEST_EXE%" (
  > "%TEST_LOG%" echo ERROR: Test executable not found: %TEST_EXE%
  echo ERROR: Test executable not found: %TEST_EXE%
  echo Test log: "%TEST_LOG%"
  exit /b 1
)

"%TEST_EXE%" > "%TEST_LOG%" 2>&1
set "TEST_EXIT=%ERRORLEVEL%"
type "%TEST_LOG%"

if not "%TEST_EXIT%"=="0" (
  echo.
  echo Test FAIL ^(exit code %TEST_EXIT%^)
  echo Detailed log: "%TEST_LOG%"
  exit /b %TEST_EXIT%
)

echo.
echo Test PASS
echo Detailed log: "%TEST_LOG%"
exit /b 0
