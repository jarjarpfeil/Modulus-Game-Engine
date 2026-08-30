$ErrorActionPreference = "Stop"
$logPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike2-run.log"
$errPath = "C:\Users\jarja\AppData\Local\Temp\kilo\spike2-err.log"

# Launch game
$proc = Start-Process -FilePath "D:\TestGame\MyGame2\Bin\Windows\Debug\MyGame2.Windows.exe" -WorkingDirectory "D:\TestGame\MyGame2\Bin\Windows\Debug" -RedirectStandardOutput $logPath -RedirectStandardError $errPath -PassThru -WindowStyle Normal
"Launched process ID: $($proc.Id)"

# Wait for game to load
Start-Sleep -Seconds 15

# Stop game
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Report sizes
$logSize = (Get-Item $logPath).Length
$errSize = (Get-Item $errPath).Length
"STDOUT: $logSize bytes / STDERR: $errSize bytes"
