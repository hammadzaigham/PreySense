@echo off
if not "%1"=="am_admin" (powershell start -verb runas '%0' am_admin & exit /b)

echo ==================================================
echo Restoring Acer Services...
echo ==================================================

echo 1. Stopping services cleanly via Service Manager...
sc config AcerLightingService start= disabled 2>nul
sc config AcerServiceSvc start= disabled 2>nul
sc config AcerApplicationBaseDriver_Device start= disabled 2>nul
sc stop AcerLightingService 2>nul
sc stop AcerServiceSvc 2>nul
sc stop AcerApplicationBaseDriver_Device 2>nul

echo Terminating helper app instances...
taskkill /IM PreySense.exe /F 2>nul
taskkill /IM AcerAgentService.exe /F 2>nul
taskkill /IM AcerCentralService.exe /F 2>nul
taskkill /IM AcerHardwareService.exe /F 2>nul

echo Waiting for services to exit...
:waitloop
tasklist /FI "IMAGENAME eq AcerLightingService.exe" 2>nul | find /I /N "AcerLightingService.exe" >nul
if "%ERRORLEVEL%"=="0" (
    echo AcerLightingService is still running, waiting...
    timeout /t 1 /nobreak >nul
    goto waitloop
)
:waitloop2
tasklist /FI "IMAGENAME eq AcerService.exe" 2>nul | find /I /N "AcerService.exe" >nul
if "%ERRORLEVEL%"=="0" (
    echo AcerService is still running, waiting...
    timeout /t 1 /nobreak >nul
    goto waitloop2
)

echo Terminating any leftover service processes...
taskkill /IM AcerLightingService.exe /F 2>nul
taskkill /IM AcerService.exe /F 2>nul
taskkill /IM AcerServiceWrapper.exe /F 2>nul

echo.
echo 2. Restoring files from backup...

rem Copy AcerLightingService files
mkdir "C:\Program Files\AcerLightingService" 2>nul
for /d %%i in ("%~dp0predatorservice.inf_amd64_*") do (
    echo Restoring AcerLightingService from "%%i"...
    xcopy /E /I /H /Y "%%i" "C:\Program Files\AcerLightingService"
)

rem Copy AcerServiceSvc files
mkdir "C:\Program Files\AcerService" 2>nul
for /d %%i in ("%~dp0acerservicecomponent.inf_amd64_*") do (
    echo Restoring AcerServiceSvc from "%%i"...
    xcopy /E /I /H /Y "%%i" "C:\Program Files\AcerService"
)

rem Copy AcerApplicationBaseDriver files
mkdir "C:\Program Files\AcerApplicationBaseDriver" 2>nul
for /d %%i in ("%~dp0acerapplicationbasedriver.inf_amd64_*") do (
    echo Restoring AcerApplicationBaseDriver from "%%i"...
    xcopy /E /I /H /Y "%%i" "C:\Program Files\AcerApplicationBaseDriver"
)

echo.
echo Installing Driver Packages into Driver Store via pnputil...
for /r "%~dp0" %%i in (*.inf) do (
    echo Registering and installing driver "%%i"...
    pnputil.exe /add-driver "%%i" /install
)

echo.
echo 3. Recreating services in Service Manager...
sc delete AcerLightingService 2>nul
sc delete AcerServiceSvc 2>nul
sc delete AcerApplicationBaseDriver_Device 2>nul
sc delete AASSvc 2>nul

sc create AcerLightingService binPath= "C:\Program Files\AcerLightingService\AcerLightingService.exe" start= auto DisplayName= "Acer Lighting Service" 2>nul
sc create AcerServiceSvc binPath= "C:\Program Files\AcerService\AcerServiceWrapper.exe" start= auto DisplayName= "Acer Service" 2>nul
sc create AcerApplicationBaseDriver_Device type= kernel binPath= "C:\Program Files\AcerApplicationBaseDriver\AcerApplicationBaseDriver.sys" start= system DisplayName= "Acer Application Base Driver" 2>nul
sc create AASSvc binPath= "C:\Program Files\AcerLightingService\AcerCentralService.exe" start= auto DisplayName= "Acer Agent Service" 2>nul

echo.
echo 4. Importing detailed registry settings...
if exist "%~dp0AcerLightingService.reg" reg import "%~dp0AcerLightingService.reg"
if exist "%~dp0AcerServiceSvc.reg" reg import "%~dp0AcerServiceSvc.reg"
if exist "%~dp0AcerApplicationBaseDriver_Device.reg" reg import "%~dp0AcerApplicationBaseDriver_Device.reg"

echo.
echo 5. Correcting service registry parameters...
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerLightingService" /v ImagePath /t REG_EXPAND_SZ /d "C:\Program Files\AcerLightingService\AcerLightingService.exe" /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerLightingService" /v Start /t REG_DWORD /d 2 /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerServiceSvc" /v ImagePath /t REG_EXPAND_SZ /d "C:\Program Files\AcerService\AcerServiceWrapper.exe" /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerServiceSvc" /v Start /t REG_DWORD /d 2 /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerApplicationBaseDriver_Device" /v ImagePath /t REG_EXPAND_SZ /d "\??\C:\Program Files\AcerApplicationBaseDriver\AcerApplicationBaseDriver.sys" /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AcerApplicationBaseDriver_Device" /v Start /t REG_DWORD /d 1 /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AASSvc" /v ImagePath /t REG_EXPAND_SZ /d "C:\Program Files\AcerLightingService\AcerCentralService.exe" /f
reg add "HKLM\SYSTEM\CurrentControlSet\Services\AASSvc" /v Start /t REG_DWORD /d 2 /f

echo.
echo 6. Refreshing and starting services...
sc config AcerLightingService start= auto 2>nul
sc config AcerServiceSvc start= auto 2>nul
sc config AcerApplicationBaseDriver_Device start= system 2>nul
sc config AASSvc start= auto 2>nul
net start AcerLightingService 2>nul
net start AcerServiceSvc 2>nul
net start AcerApplicationBaseDriver_Device 2>nul
net start AASSvc 2>nul

echo ==================================================
echo Done! Service restore completed.
echo ==================================================
pause
