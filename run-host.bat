@echo off
cd /d D:\Modulus-Game-Engine\tests\integration\space-escape-mods\ModReassemblyHost\bin\Debug
ModReassemblyHost.exe > host-output.log 2>&1
echo Exit code: %ERRORLEVEL% >> host-output.log
