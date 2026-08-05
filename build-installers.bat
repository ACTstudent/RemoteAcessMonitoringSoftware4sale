@echo off
title CAMS Installer Builder
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installers.ps1"
pause