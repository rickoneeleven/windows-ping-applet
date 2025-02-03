$assemblyFile = Join-Path $PSScriptRoot "Properties\AssemblyInfo.cs"
$timestamp = Get-Date -Format "dd/MM/yy HH:mm"
$content = Get-Content $assemblyFile -Raw
$newContent = $content -replace '\[assembly: AssemblyMetadata\("BuildTimestamp", ".*?"\)\]', "[assembly: AssemblyMetadata(`"BuildTimestamp`", `"$timestamp`")]"
Set-Content -Path $assemblyFile -Value $newContent -NoNewline