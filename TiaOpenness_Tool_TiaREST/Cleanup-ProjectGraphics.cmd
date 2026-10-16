@echo off
setlocal
REM Starts Cleanup-ProjectGraphics.ps1 without changing the machine ExecutionPolicy.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cleanup-ProjectGraphics.ps1" %*
set EXITCODE=%ERRORLEVEL%
if %EXITCODE% neq 0 pause
exit /b %EXITCODE%
