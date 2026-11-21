# PlateWatcher — Lector de matrículas de camiones (.NET 10)

Worker Service en .NET 10 que vigila una carpeta donde las cámaras de la entrada
guardan las fotos de los camiones, reconoce la matrícula con OCR local
(Tesseract, sin depender de servicios en la nube) y la envía a una API externa
por REST con autenticación por API key.

## Cómo funciona

1. Las cámaras escriben las fotos (`*.jpg` por defecto) en la carpeta `Watcher:InputFolder`.
2. El servicio detecta cada imagen nueva (FileSystemWatcher + un escaneo inicial al arrancar),
   espera a que el archivo esté completamente escrito y lo procesa.
3. `TesseractPlateReader` ejecuta OCR (modos de página 6 y 11, configurables) sobre la imagen
   y `PlateTextExtractor` busca matrículas válidas con una regex configurable.
   Si no hay coincidencias, reintenta corrigiendo confusiones típicas de OCR
   (`O→0`, `I/L→1`, `S→5`, `B→8`, `Z→2`, `Q→0`).
4. `RestPlateSender` envía el resultado a la API externa vía `HttpClient` con
   reintentos automáticos (`Microsoft.Extensions.Http.Resilience`):

```json
POST /api/placas
X-API-Key: <tu-clave>
{
  "matricula": "1234-ABC",
  "fechaCapturaUtc": "2025-11-21T10:15:30Z",
  "archivoOrigen": "IMG_0042.jpg",
  "imagenBase64": null
}
```

5. Cada foto se mueve a `Watcher:ProcessedFolder` (envío OK) o `Watcher:FailedFolder`
   (sin matrícula reconocible o con la API caída), así no se pierde ninguna.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Datos de entrenamiento de Tesseract: descarga `spa.traineddata`
  (y opcionalmente `eng.traineddata`) desde el repositorio
  [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) o
  [tessdata_best](https://github.com/tesseract-ocr/tessdata_best) y cópialos en
  `src/PlateWatcher.Worker/tessdata/` (o la carpeta que apunte `Ocr:TessDataPath`).

## Configuración (`appsettings.json`)

| Sección | Clave | Descripción |
|---|---|---|
| `Watcher` | `InputFolder` / `ProcessedFolder` / `FailedFolder` | Carpetas de trabajo (relativas al ejecutable si no son absolutas) |
| `Watcher` | `FilePattern` | Patrón de archivos a vigilar (`*.jpg`, `*.png`, …) |
| `Watcher` | `MaxDegreeOfParallelism` | Imágenes procesadas en paralelo (1 = orden estricto) |
| `Ocr` | `TessDataPath` / `Language` | Carpeta tessdata e idioma (`spa`, `spa+eng`) |
| `Ocr` | `PlateRegex` | Formatos de matrícula aceptados — ajústalo a tu país |
| `Ocr` | `PageSegModes` | Modos de segmentación de página de Tesseract a probar |
| `ExternalApi` | `BaseUrl` | Raíz de la API externa (obligatoria y absoluta; con validación al arrancar) |
| `ExternalApi` | `Endpoint` | Ruta relativa del POST |
| `ExternalApi` | `ApiKeyHeader` / `ApiKey` | Cabecera y valor de autenticación (usa *user-secrets* o variables de entorno; no lo subas al repo) |

Ejemplo de regex por defecto: cubre `1234-ABC` / `1234 ABC` (España),
`AB123CD` (Mercosur), `ABC-1234` y series numéricas de 6–8 dígitos.

## Ejecutar

```bash
dotnet build PlateWatcher.sln
dotnet test PlateWatcher.sln
dotnet run --project src/PlateWatcher.Worker
```

Prueba rápida: coloca una foto con matrícula en `bin/Debug/net10.0/input/`
y observa el log y las carpetas `processed/` / `failed/`.

> Si Tesseract se queja de que no encuentra `tessdata`: en Windows el paquete
> NuGet incluye los binarios nativos; en Linux instala `libtesseract-dev`,
> `libleptonica-dev` y `tesseract-ocr-spa`.

### Producción

- **Servicio de Windows**: añade el paquete
  `Microsoft.Extensions.Hosting.WindowsServices`, llama a
  `builder.Services.AddWindowsService()` y registra el exe con
  `sc create PlateWatcher binPath= "C:\PlateWatcher\PlateWatcher.Worker.exe"`.
- **systemd**: paquete `Microsoft.Extensions.Hosting.Systemd` +
  `builder.Services.AddSystemd()` y una unidad equivalente.
- **Docker**: imagen base `mcr.microsoft.com/dotnet/runtime:10.0` (Debian) con
  `apt-get install -y --no-install-recommends libtesseract5 libleptonica-dev
  tesseract-ocr-spa`, montando la carpeta de entrada como volumen.

En todos los casos la `ApiKey` se inyecta con `dotnet user-secrets`, la variable
de entorno `ExternalApi__ApiKey` o el almacén de secretos de tu plataforma.

## Estructura

```
src/PlateWatcher.Worker
├── Options/            WatcherOptions, OcrOptions, ExternalApiOptions
├── Models/             PlateObservation, PlateRecognitionResult, PlateDispatchResult, PlatePayload
├── Services/
│   ├── IPlateReader / TesseractPlateReader   OCR local
│   ├── IPlateSender / RestPlateSender        envío REST a la API externa
│   └── PlateTextExtractor                    normalización + regex de matrículas
└── Workers/FolderWatchService                carpeta vigilada + cola + movimiento de archivos
tests/PlateWatcher.Tests                      tests unitarios de extracción y envío
```
