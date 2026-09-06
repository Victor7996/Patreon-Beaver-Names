@echo off
title Patreon Mock API Server (Port 3000)
cd /d "%~dp0mock-api"
echo Starting Patreon Mock API server...
node server.js
pause
