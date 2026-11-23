# Contrato para un servidor de IA propio (`GenerativeAi__Provider=Http`)

Holograma3D puede usar tu propio servidor imagen → 3D (por ejemplo, un wrapper de TripoSR o Hunyuan3D-2 en tu GPU) en lugar de Meshy. Basta con que exponga estos tres endpoints bajo `GenerativeAi__BaseUrl`.

Si `GenerativeAi__ApiKey` tiene valor, cada llamada lleva `Authorization: Bearer {ApiKey}`.

## 1. Crear un trabajo

```
POST {BaseUrl}/jobs
Content-Type: multipart/form-data
```

- Campo `images`, repetido de 1 a `GenerativeAi__MaxImages` veces. Cada parte es una foto JPEG, PNG o WEBP de hasta 10 MB, con su `filename` y su `Content-Type`.
- Las fotos llegan en orden: frente, derecha, atrás, izquierda.

Respuesta `202 Accepted` (sirve cualquier 2xx):

```json
{ "id": "abc123" }
```

## 2. Consultar el estado

```
GET {BaseUrl}/jobs/{id}
```

```json
{ "status": "running", "progress": 40, "error": null }
```

- `status`: `queued` | `running` | `succeeded` | `failed`. Cualquier otro valor se trata como `queued`.
- `progress`: 0 a 100.
- `error`: mensaje legible para el usuario cuando `status` es `failed`.

Holograma3D consulta cada `GenerativeAi__PollSeconds` segundos. Si el trabajo no termina en `GenerativeAi__TimeoutMinutes` minutos, lo da por fallido.

## 3. Descargar el resultado

```
GET {BaseUrl}/jobs/{id}/result
```

- Responde con el binario del modelo en el formato de `GenerativeAi__OutputFormat`: `glb` (`model/gltf-binary`) o `stl` (`model/stl`).
- Máximo 50 MB.
- Holograma3D valida el archivo por su contenido antes de guardarlo en el catálogo del usuario.

## Errores

- Cualquier respuesta que no sea 2xx al crear el trabajo se muestra al usuario como 502.
- Al consultar o descargar, un error de red o una respuesta que no sea 2xx (429, 5xx) se reintenta en el siguiente ciclo.
