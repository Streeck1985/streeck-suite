@echo off
title Streeck Suite
echo ========================================================
echo   Iniciando Streeck Suite
echo ========================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_suite.ps1"
pause
