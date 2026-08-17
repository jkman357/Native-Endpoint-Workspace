@echo off
setlocal
cd /d "%~dp0"
set "APP=NativeEndpointWorkspace\bin\Release\NativeEndpointWorkspace.exe"
if not exist "%APP%" (
  echo Release executable not found. Running build.cmd first...
  call build.cmd
  if errorlevel 1 exit /b 1
)
start "" "%APP%"
