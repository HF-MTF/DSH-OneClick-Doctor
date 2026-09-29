@echo off
cd /d "%~dp0"
echo ============================================
echo  DSH Doctor - Build
echo ============================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
echo.
echo Output: DSH-Doctor exe in this folder
pause