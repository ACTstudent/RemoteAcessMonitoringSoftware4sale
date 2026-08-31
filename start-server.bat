@echo off
title CAMS Server
cd /d "%~dp0"
echo Starting CAMS Server on https://localhost:5000
echo The dashboard opens automatically in your browser.
echo Close this window to stop the server.
echo.
"Server.exe"
