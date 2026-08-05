@echo off
title CAMS Server
cd /d "%~dp0"
echo Starting CAMS Server on http://localhost:5000
echo.
echo Open http://localhost:5000/Admin in a browser.
echo Close this window to stop the server.
echo.
start "" "Server.exe"
pause