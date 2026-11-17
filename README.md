# Holograma3D

Control por gestos de un modelo 3D que se proyecta como holograma de 4 lados (pirámide tipo Pepper's ghost sobre un móvil).

| App | Qué hace | URL de desarrollo |
|-----|----------|-------------------|
| `Holograma3D.Api` | Núcleo hexagonal, REST y SignalR | http://localhost:5080 |
| `Holograma3D.Control` | PC con cámara: gestos, modelos y emparejamiento | http://localhost:5081 |
| `Holograma3D.Display` | Móvil o pantalla: proyección de 4 lados | `http://<IP-LAN>:5082` |

## Estructura

```
src/Holograma3D.Core            Dominio, casos de uso y puertos (solo System.*)
src/Holograma3D.Infrastructure  Adaptadores de salida
src/Holograma3D.Api             Adaptadores de entrada y composición
src/Holograma3D.Control         Webapp de control (estáticos)
src/Holograma3D.Display         Webapp del proyector (estáticos)
tests/Holograma3D.Tests         xUnit
```

## Configuración personal

Copia `.env.example` a `.env` y complétalo. `.env` está en `.gitignore`: ahí van tu cadena de conexión y tus claves, nunca en `appsettings*.json`.

```powershell
Copy-Item .env.example .env
```

## Cómo correr

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

`run.ps1` carga `.env`, muestra la URL del Display para el móvil y abre una ventana por app. También puedes arrancar una sola:

```powershell
dotnet run --project src/Holograma3D.Api
```

Pruebas:

```powershell
dotnet test Holograma3D.slnx
```

## Móvil en la misma red

El móvil entra a la Api (5080) y al Display (5082) por la IP LAN del PC. Si no conecta, abre esos puertos en el firewall de Windows desde una consola de administrador:

```powershell
New-NetFirewallRule -DisplayName "Holograma3D (5080, 5082)" -Direction Inbound -Protocol TCP -LocalPort 5080,5082 -Action Allow -Profile Private
```

La cámara del Control solo funciona en contexto seguro, así que ábrelo siempre en `http://localhost:5081`.
