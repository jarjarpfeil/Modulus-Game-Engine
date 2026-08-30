$log = Get-Content "C:\Users\jarja\AppData\Local\Temp\kilo\spike4-run.txt" -Raw -ErrorAction SilentlyContinue
if ($log) {
    Write-Output "=== LOG CONTENTS ==="
    Write-Output $log
    Write-Output "=== END LOG ==="
} else {
    Write-Output "ERROR: Log file not found"
}
