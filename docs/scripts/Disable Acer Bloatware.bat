@echo off
if not "%1"=="am_admin" (powershell start -verb runas '%0' am_admin & exit /b)

rem PreySense depends on AcerServiceSvc, AcerLightingService, and AcerQAAgentSvis.
rem Do not disable those services: they provide the AcerService socket, RGB/mode routing,
rem and Quick Access mode-key state used by the app.
sc config "AcerCCAgentSvis" start= disabled
sc config "AcerDeviceEnablingServiceV2" start= disabled
sc config "AcerDIAgentSvis" start= disabled
rem sc config "ASMSvc" start= disabled
sc config "PredatorService" start= disabled
schtasks /Change /TN "PredatorSenseLauncher" /Disable
taskkill /IM PredatorSense.exe /F >nul 2>&1
taskkill /IM PredatorSenseApp.exe /F >nul 2>&1
taskkill /IM PredatorSenseLauncher.exe /F >nul 2>&1
taskkill /IM PSLauncher.exe /F >nul 2>&1

echo Reboot computer to apply changes
pause
