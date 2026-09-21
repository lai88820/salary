@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Starting payslip system... (keep the minimized window open)
start "PayslipApi - close this window to stop" /min PayslipApi.exe
timeout /t 4 /nobreak >nul
start "" http://localhost:5080
