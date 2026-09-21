@echo off
chcp 65001 >nul
cd /d "%~dp0"

where python >nul 2>nul
if errorlevel 1 (
  echo 找不到 Python。請先到 https://www.python.org/downloads/ 安裝，
  echo 安裝時要勾選「Add python.exe to PATH」。
  pause
  exit /b 1
)

python -c "import openpyxl, msoffcrypto" 2>nul
if errorlevel 1 (
  echo 第一次使用，安裝需要的套件...
  python -m pip install --quiet openpyxl msoffcrypto-tool
)

set "EXCEL=%~1"
if "%EXCEL%"=="" (
  echo 把每月的薪資 Excel 檔「拖曳」到這個 bat 檔上就能轉換。
  echo.
  set /p "EXCEL=或是在這裡貼上 Excel 路徑後按 Enter： "
)
set "EXCEL=%EXCEL:"=%"

set "PWD_ARG="
set /p "XLPWD=Excel 密碼（沒有密碼直接按 Enter）： "
if not "%XLPWD%"=="" set "PWD_ARG=--password "%XLPWD%""

echo.
python "%~dp0excel_to_sql.py" "%EXCEL%" %PWD_ARG%
echo.
pause
