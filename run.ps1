# Arranca Api, Control y Display, cada una en su ventana, con la configuración de .env.
$root = $PSScriptRoot

$envFile = Join-Path $root '.env'
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile -Encoding UTF8) {
        # CLAVE=valor; se ignoran comentarios, líneas vacías y claves sin valor. Nunca se imprimen los valores.
        if ($line -match '^\s*([^#=\s][^=]*?)\s*=(.*)$' -and $Matches[2].Trim()) {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2].Trim(), 'Process')
        }
    }
    Write-Host 'Configuración cargada desde .env'
} else {
    Write-Host 'No hay .env: copia .env.example a .env y complétalo.' -ForegroundColor Yellow
}

$ip = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.PrefixOrigin -eq 'Dhcp' } |
    Select-Object -First 1 -ExpandProperty IPAddress
if ($ip) {
    Write-Host "Display para el móvil: http://${ip}:5082"
} else {
    Write-Host 'No se detectó una IP LAN por DHCP; revisa ipconfig.' -ForegroundColor Yellow
}

# Las ventanas heredan las variables cargadas arriba.
foreach ($app in 'Holograma3D.Api', 'Holograma3D.Control', 'Holograma3D.Display') {
    Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command', "dotnet run --project src/$app"
}
