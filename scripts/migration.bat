@echo off
rem Delegates to migration.ps1 so the seal guards live in one place.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0migration.ps1" %*
exit /b %errorlevel%
