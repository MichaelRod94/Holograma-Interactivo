# Grabaciones de manos

Grabaciones reales de los HandFrames que el Control envía a la Api. `RecordingReplayTests` las reproduce en el `GestureInterpreter` con la calibración de `src/Holograma3D.Api/appsettings.json`. Si falta un archivo, su test aparece como omitido.

## Cómo grabar

1. Abre el Control (`http://localhost:5081`) con la cámara funcionando.
2. Pulsa **Grabar 30 s** y haz el gesto. Al terminar, el navegador descarga `grabacion-<fecha>.json`.
3. Renómbralo como se indica abajo y cópialo en esta carpeta.

| Archivo | Qué hacer durante los 30 s | El test espera |
|---|---|---|
| `manos-abiertas-quietas.json` | Una o dos manos abiertas, quietas o moviéndose despacio, sin cerrar el puño | Ningún Grab |
| `agarrar-mover-soltar.json` | Mano abierta, cerrar el puño **una vez**, moverlo, abrir la mano y quedarse con la mano abierta o sin manos | Exactamente un Grab y termina en Idle |

Las grabaciones no tienen datos personales (solo 21 puntos por mano), así que sí van en el repositorio.
