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

## Base de datos (PostgreSQL 18 local)

Una sola vez. Elige una contraseña para el rol de la app y úsala en los tres pasos; psql te pide la del superusuario `postgres`.

1. Crea el rol:

   ```powershell
   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -c "CREATE ROLE holograma3d LOGIN CREATEDB PASSWORD '<tu-contraseña>';"
   ```

2. En `.env`, completa `ConnectionStrings__Holograma3D=Host=localhost;Port=5432;Database=holograma3d;Username=holograma3d;Password=<tu-contraseña>`.

3. Guarda la misma cadena en user-secrets del Api, para que `dotnet ef` la encuentre:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:Holograma3D" "Host=localhost;Port=5432;Database=holograma3d;Username=holograma3d;Password=<tu-contraseña>" --project src/Holograma3D.Api
   ```

Los archivos de los modelos no van en la base: se guardan en `src/Holograma3D.Api/storage/models/` (ignorado por git; cambia la carpeta con `Storage__Root` en `.env`).

La Api aplica las migraciones al arrancar en Development. También puedes aplicarlas a mano:

```powershell
dotnet ef database update --project src/Holograma3D.Infrastructure --startup-project src/Holograma3D.Api
```

Para agregar una migración cuando cambie el modelo:

```powershell
dotnet ef migrations add <Nombre> --project src/Holograma3D.Infrastructure --startup-project src/Holograma3D.Api --output-dir Persistence/Migrations
```

## Cómo correr

```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

`run.ps1` carga `.env`, muestra la URL del Display para el móvil y abre una ventana por app. También puedes arrancar una sola:

```powershell
dotnet run --project src/Holograma3D.Api
```

Pruebas (los tests de integración usan tu PostgreSQL local con la cadena de user-secrets: cada corrida crea una base `holograma3d_test_*` y la borra al terminar):

```powershell
dotnet test Holograma3D.slnx
```

`src/Holograma3D.Api/Holograma3D.Api.http` trae las llamadas de registro, login, `/me`, refresh y logout-all para probar a mano desde Visual Studio o VS Code (extensión REST Client).

## Display (proyector)

Abre `http://<IP-LAN>:5082/?s=CODIGO` (lo hace el QR del Control), elige la vista y toca "Iniciar". Sin cuenta: entra con el código de la sesión.

- **Holograma 4 lados**: las 4 vistas en cruz para la pirámide sobre el móvil.
- **Plana**: una sola vista de frente a pantalla completa, para proyectar en una pared, una TV o un proyector. Responde a los mismos gestos.

La vista elegida se guarda en el navegador. Para cambiarla en marcha, toca la pantalla: aparece unos segundos el botón "Ver plana" / "Ver holograma".

| Parámetro | Para qué |
|-----------|----------|
| `?s=CODIGO` | Código de la sesión; sin él, el Display lo pide |
| `?demo=1` | Sin Api: gira el modelo por defecto, para probar la pirámide |
| `?calib=1` | Abre la calibración (también con tres toques rápidos) |
| `?hud=1` | Muestra la conexión y el gesto en una esquina |

En marcha, un toque largo vuelve a la pantalla inicial. En la calibración, el "Cubo de prueba" ayuda a orientar la pirámide: desde el lado sur debe verse la cara roja de frente y la verde a la derecha. Los valores se guardan en el navegador del móvil.

## Móvil en la misma red

El móvil entra a la Api (5080) y al Display (5082) por la IP LAN del PC. Si no conecta, abre esos puertos en el firewall de Windows desde una consola de administrador:

```powershell
New-NetFirewallRule -DisplayName "Holograma3D (5080, 5082)" -Direction Inbound -Protocol TCP -LocalPort 5080,5082 -Action Allow -Profile Private
```

La cámara del Control solo funciona en contexto seguro, así que ábrelo siempre en `http://localhost:5081`.

El QR de cada sesión apunta al Display en la IP LAN que detecta la Api (la imprime al arrancar: "El QR de las sesiones apunta a…"). Si detecta otra interfaz, fija la URL con `PublicDisplayUrl` en `.env`.

Los valores de calibración de gestos están en la sección `Gestures` de `src/Holograma3D.Api/appsettings.json`; se leen al arrancar la Api.
