@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo.
echo ===== [1/3] Build Angular =====
pushd "..\payslip-web"
call npm run build
if errorlevel 1 goto :err
popd

echo.
echo ===== [2/3] Copy frontend to wwwroot =====
if exist wwwroot rmdir /s /q wwwroot
xcopy /e /i /y "..\payslip-web\dist\payslip-web\browser" wwwroot >nul
if errorlevel 1 goto :err

echo.
echo ===== [3/3] Publish (no .NET install needed on target PC) =====
if exist "..\PayslipApp" rmdir /s /q "..\PayslipApp"
dotnet publish PayslipApi.csproj -c Release -r win-x64 --self-contained true -o "..\PayslipApp"
if errorlevel 1 goto :err

copy /y "start-payslip.bat" "..\PayslipApp\" >nul
copy /y "payroll_11508.sql" "..\PayslipApp\" >nul

echo.
echo Done!  Folder: %~dp0..\PayslipApp
echo Copy the whole PayslipApp folder to the other laptop.
pause
exit /b 0

:err
echo.
echo *** FAILED - see messages above ***
pause
exit /b 1
