# Mantiene el candado qa-run y el stack fin arriba hasta que exista el centinela (max 40 min).
param([string]$Stop = "$env:TEMP\qa-fin-release.flag")
Remove-Item $Stop -ErrorAction SilentlyContinue
$t = [Diagnostics.Stopwatch]::StartNew()
"HOLD START $(Get-Date -Format o)" | Out-File "$env:TEMP\qa-fin-hold.log"
while (-not (Test-Path $Stop) -and $t.Elapsed.TotalMinutes -lt 40) { Start-Sleep 3 }
"HOLD END $(Get-Date -Format o)" | Out-File "$env:TEMP\qa-fin-hold.log" -Append
