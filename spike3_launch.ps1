$ErrorActionPreference = 'Continue'

$logPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike3-run.log"
$errPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike3-err.log"

Write-Host "Launching MyGame2.Windows.exe..."
$proc = Start-Process -FilePath "D:\TestGame\MyGame2\Bin\Windows\Debug\MyGame2.Windows.exe" -WorkingDirectory "D:\TestGame\MyGame2\Bin\Windows\Debug" -RedirectStandardOutput $logPath -RedirectStandardError $errPath -PassThru -WindowStyle Normal
Write-Host "Process launched: PID=$($proc.Id)"

Write-Host "Waiting 15 seconds for scene to load..."
Start-Sleep -Seconds 15

Write-Host "Stopping process $($proc.Id)..."
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

$logSize = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
$errSize = if (Test-Path $errPath) { (Get-Item $errPath).Length } else { 0 }
Write-Host "`nSTDOUT: $logSize bytes / STDERR: $errSize bytes"

# Copy logs to .txt for reading
Copy-Item $logPath "C:\Users\jarja\AppData\Local\Temp\kilo\spike3-run.txt" -Force
Copy-Item $errPath "C:\Users\jarja\AppData\Local\Temp\kilo\spike3-err.txt" -Force
Write-Host "`nLogs copied to .txt. spike3-run.txt: $logSize bytes"
