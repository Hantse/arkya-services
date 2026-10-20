@echo off
setlocal
set "PSModulePath=%WINDIR%\System32\WindowsPowerShell\v1.0\Modules;%ProgramFiles%\WindowsPowerShell\Modules"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Agent.ps1" %*
set "installerExit=%ERRORLEVEL%"
pause
exit /b %installerExit%
