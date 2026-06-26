@echo off
if not "%1"=="am_admin" (powershell start -verb runas '%0' am_admin & exit /b)

rem Restore the Acer/Predator Sense services that the disable script may have changed.
sc config "AcerCCAgentSvis" start= auto
sc config "AcerDeviceEnablingServiceV2" start= auto
sc config "AcerDIAgentSvis" start= auto
sc config "AcerLightingService" start= auto
sc config "AcerQAAgentSvis" start= auto
sc config "AcerServiceSvc" start= auto
sc config "ASMSvc" start= auto
sc config "PredatorService" start= auto
schtasks /Change /TN "PredatorSenseLauncher" /Enable

echo Reboot computer to apply changes
pause
