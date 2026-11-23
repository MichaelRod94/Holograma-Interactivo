# Demo de Holograma3D

Checklist para ensayar la demo completa con tu propio `.env`, sin tocar código. Marca cada paso en el ensayo.

## Preparación (una vez por equipo)

- [ ] .NET 10 SDK y PostgreSQL 18 instalados; el servicio `postgresql-x64-18` corriendo (`Get-Service postgresql-x64-18`).
- [ ] `.env` creado desde `.env.example` (`Copy-Item .env.example .env`) con `ConnectionStrings__Holograma3D` (ver la sección de base de datos del README).
- [ ] Para la demo con IA: `GenerativeAi__Provider=Fake` para ensayar; `Meshy` y `GenerativeAi__ApiKey` para la demo real.
- [ ] Opcional: `src/Holograma3D.Api/models/u2netp.onnx` para "Crear desde 5 fotos" con fondos menos cuidados.
- [ ] Regla de firewall para 5080 y 5082 (consola de administrador; ver README).
- [ ] PC y móvil en la misma Wi-Fi (no la de invitados: suele aislar los dispositivos).
- [ ] Pirámide armada con [la plantilla](piramide.svg).

## Ensayo

1. [ ] `powershell -ExecutionPolicy Bypass -File .\run.ps1`. En la ventana de la Api deben aparecer "Configuración: …\.env" y "El QR de las sesiones apunta a http://<IP>:5082".
2. [ ] Abre `http://localhost:5081`, crea una cuenta y permite la cámara. Arriba a la izquierda del video: ≥ 20 fps.
3. [ ] Escanea el QR con el móvil, elige "Holograma 4 lados" y toca "Iniciar". El Control marca "Proyectores conectados: 1".
4. [ ] Coloca la pirámide en el centro de la cruz. Tres toques en el móvil → "Cubo de prueba": desde el lado sur se ve la cara roja de frente y la verde a la derecha (si no, "Espejo" / "Arriba hacia dentro").
5. [ ] Prueba los 4 gestos y mira el indicador del Control:
   - Puño y mover → el modelo se mueve ("Agarrando"); abrir la mano lo suelta ("Libre").
   - Dos palmas separándose o juntándose → zoom.
   - ✌️ con una mano → rotación: moverla gira el modelo y girar la muñeca lo inclina. Dos puños también rotan.
   - Tecla R → vuelve al centro.
   - RTT en el Control < 100 ms.
6. [ ] "Crear con IA" con `Provider=Fake`: 1–4 fotos → "Lista" en segundos → "Proyectar".
7. [ ] Cambia a `Provider=Meshy` en `.env`, reinicia con `run.ps1` y repite con 2–4 fotos reales de un objeto (tarda unos minutos y consume créditos).
8. [ ] "Crear desde 5 fotos" con un objeto simple sobre fondo liso → STL en menos de 10 s → "Proyectar" y "Descargar STL".
9. [ ] Recarga el Control: vuelve la misma sesión (mismo código) y el móvil sigue conectado.
10. [ ] En el móvil, toca la pantalla → "Ver plana": una sola vista de frente; vuelve con "Ver holograma".

## HTTPS para el móvil con Dev Tunnels

Por HTTP en la red local, el navegador del móvil no deja mantener la pantalla encendida ni (en algunos) usar pantalla completa. Con un túnel HTTPS sí:

```powershell
winget install Microsoft.devtunnel
```

```powershell
devtunnel user login
```

```powershell
devtunnel host -p 5080 -p 5082 --allow-anonymous
```

El último comando muestra una URL por puerto, del estilo `https://<id>-5080.<región>.devtunnels.ms` y `https://<id>-5082.<región>.devtunnels.ms`. Con esas URLs, en `.env` (nunca en el repositorio):

```
Display__ApiBaseUrl=https://<id>-5080.<región>.devtunnels.ms
PublicDisplayUrl=https://<id>-5082.<región>.devtunnels.ms
Cors__Origins__0=https://<id>-5082.<región>.devtunnels.ms
```

Reinicia con `run.ps1`: el QR apunta al túnel y el Display habla con la Api por HTTPS. El Control sigue en `http://localhost:5081` contra la Api local (menos latencia). Si el túnel muestra una página de aviso, ábrela una vez en el móvil para cada URL y acepta. Cierra el túnel (Ctrl+C) al terminar: con `--allow-anonymous` cualquiera con la URL llega a la Api (que igual exige cuenta salvo para los endpoints públicos del Display).

## Robustez

Verificado en el navegador (Api, Control y dos Display):

| Prueba | Resultado |
|--------|-----------|
| Dos Display a la vez | El Control marca "Proyectores conectados: 2" y ambos siguen conectados |
| Recargar el Control | Recupera la misma sesión y los Display siguen conectados |
| Token de acceso corto (`Auth__AccessTokenMinutes=2`) | El Control lo renueva solo antes de vencer; el hub sigue funcionando aunque venza el token con el que se conectó |
| Detener la Api | Control: "Reconectando con la Api…" y luego "Se perdió la conexión…"; Display: indicador ámbar y luego vuelve a su pantalla inicial con "Se perdió la conexión con la Api." |

Falta probar en el móvil:

- [ ] Apagar la Wi-Fi del móvil unos 5 s: el Display reintenta (0, 2, 10 y 30 s), vuelve a entrar con el mismo código y recupera el modelo y la posición.

Límite conocido: las sesiones viven en memoria. Si la Api se reinicia, el Control crea otra sesión sola, pero los móviles deben volver a escanear el QR.

## Rendimiento en el móvil

El Display dibuja la escena 4 veces por cuadro, así que el costo crece con los triángulos del modelo y con la resolución de los 4 paneles. Mídelo con `?hud=1` (muestra los fps).

- [ ] Con un modelo de ~500 k triángulos, el Display debe mantener ≥ 30 fps. Si no llega, en la calibración (tres toques) baja "Resolución (px)" a 512 o 384 y anota aquí el modelo de móvil y el valor que funcionó.

| Móvil | Modelo | Resolución | fps |
|-------|--------|-----------|-----|
| _(pendiente del ensayo)_ | | | |

Para la IA, `GenerativeAi__TargetPolycount=30000` (el valor por defecto) es el recomendado: se ve bien en la pirámide y deja margen en móviles de gama media. "Crear desde 5 fotos" a resolución 128 da ~50 k triángulos.

## Calibrar los gestos

Los valores están en la sección `Gestures` de `src/Holograma3D.Api/appsettings.json` y se leen al arrancar la Api (no hace falta recompilar). Ajústalos con 2 o 3 personas: cada una hace los 4 gestos durante un minuto y se cambia un valor a la vez.

| Valor | Qué hace | Actual | Si… |
|-------|----------|--------|-----|
| `GainXY` | Cuánto se mueve el modelo por cada unidad que se mueve la mano | 2.5 | se mueve poco → súbelo; cruza la pantalla con un gesto corto → bájalo |
| `GainZ` | Profundidad al acercar o alejar el puño | 1.5 | la profundidad no se nota o salta |
| `RotGain` | Radianes por unidad de desplazamiento al rotar (✌️ o dos puños) | 3.5 | gira demasiado o muy poco |
| `RotateGesture` | Seña de MediaPipe para rotar con una mano; girar la muñeca inclina el modelo 1:1 | `Victory` (✌️) | MediaPipe la confunde → prueba `Pointing_Up` (☝️), `Thumb_Up` (👍) o `ILoveYou` (🤟); vacía = solo dos puños |
| `ZoomExponent` | Sensibilidad del zoom con dos palmas | 1.0 | el zoom es brusco (bájalo) o lento (súbelo) |
| `StableFrames` | Frames seguidos que debe durar un gesto para activarse | 3 | se activa sin querer → súbelo a 4–5; reacciona tarde → bájalo a 2 |
| `DeadZone` | Movimiento mínimo que cuenta al arrastrar | 0.005 | el modelo tiembla con la mano quieta → súbelo |
| `MinScore` | Confianza mínima de MediaPipe para aceptar puño o palma | 0.6 | confunde gestos → súbelo |
| `FilterMinCutoff`, `FilterBeta` | Suavizado (One Euro): menos temblor ↔ menos retraso | 1.0, 0.02 | tiembla → baja `FilterMinCutoff`; se siente lento → sube `FilterBeta` |

Anota aquí los valores finales y el porqué después del ensayo:

| Valor | Final | Por qué |
|-------|-------|---------|
| _(pendiente del ensayo)_ | | |

## Grabaciones de manos para los tests

El botón **Grabar 30 s** del Control guarda en un JSON los frames que se envían a la Api. Las dos grabaciones que piden los tests (manos abiertas quietas; agarrar-mover-soltar) se explican en [tests/Holograma3D.Tests/Recordings/README.md](../tests/Holograma3D.Tests/Recordings/README.md). Sirven también para comparar calibraciones: el test reproduce la grabación con los valores de `appsettings.json`.

## Respaldo

La base y los archivos de los modelos se respaldan por separado (los modelos están en disco, en `Storage__Root`):

```powershell
& "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -U holograma3d -h localhost -d holograma3d -Fc -f "holograma3d-$(Get-Date -Format yyyyMMdd).dump"
```

```powershell
Compress-Archive -Path src/Holograma3D.Api/storage -DestinationPath "storage-$(Get-Date -Format yyyyMMdd).zip"
```

Para restaurar en una base vacía:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe" -U holograma3d -h localhost -d holograma3d --clean --if-exists "holograma3d-AAAAMMDD.dump"
```

`pg_dump` y `pg_restore` piden la contraseña del rol `holograma3d`, que está en tu `.env`.
