@echo off
setlocal EnableDelayedExpansion

:: 1. Accept printer name from Unity as an argument (%~1)
set "PRINTER_NAME=%~1"

:: If no name was sent from Unity, fallback to the list (manual mode)
if "%PRINTER_NAME%"=="" (
    echo [Manual Mode] No printer name provided. Listing available printers...
    set /a count=0
    for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "Get-Printer | ForEach-Object { $_.Name.Trim() }"`) do (
        set /a count+=1
        set "printer[!count!]=%%A"
        echo !count!. %%A
    )
    echo.
    set /p choice=Enter printer number: 
    call set "PRINTER_NAME=%%printer[!choice!]%%"
)

if "%PRINTER_NAME%"=="" (
    echo Invalid selection. Exiting.
    exit /b 1
)

echo.
echo Selected Printer: %PRINTER_NAME%
echo Opening Printing Preferences...

:: Open the preferences window
start "" rundll32 printui.dll,PrintUIEntry /e /n "%PRINTER_NAME%"

:: 2. Smart Wait & Keys (Using PowerShell for stability)
:: It waits up to 10 seconds for the window to appear, then sends keys.
powershell -NoProfile -ExecutionPolicy Bypass ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$found = $false; " ^
  "$title = '%PRINTER_NAME% Printing Preferences'; " ^
  "for ($i=0; $i -lt 50; $i++) { " ^
  "  if ($ws.AppActivate($title)) { $found = $true; break } " ^
  "  Start-Sleep -Milliseconds 200; " ^
  "} " ^
  "if ($found) { " ^
  "  Start-Sleep -Milliseconds 500; " ^
  "  1..5 | ForEach-Object { $ws.SendKeys('{TAB}'); Start-Sleep -Milliseconds 100 }; " ^
  "  $ws.SendKeys(' '); Start-Sleep -Milliseconds 150; " ^
  "  $ws.SendKeys('~'); " ^
  "  Write-Host 'Success: Keys sent to' $title; " ^
  "} else { " ^
  "  Write-Host 'Error: Could not find' $title; " ^
  "  exit 1; " ^
  "}"

:: Only pause if we are in manual mode
if "%~1"=="" pause
exit /b 0
