@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0keycloak-export.ps1" %*
exit /b %errorlevel%
