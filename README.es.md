<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Icono de Arşiv Ara">

# Arşiv Ara

**Busca dentro de archivos ZIP, RAR, 7z y TAR al instante, sin descomprimirlos.**

Arrastra tus archivos comprimidos, escribe unas letras y ve cada archivo que coincide en milisegundos.

[English](README.md) · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · **Español**

[![Release](https://img.shields.io/github/v/release/YOUR_GITHUB_USER/ArsivAra?color=F7861F&label=versi%C3%B3n)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-0B1B2E)](LICENSE)

<img src="docs/images/grid.png" width="860" alt="Vista de cuadrícula con miniaturas de fotos e iconos de Windows">

</div>

## Por qué

Las copias de seguridad, carpetas de proyectos y descargas acaban convertidas en decenas de archivos comprimidos grandes. Encontrar un solo archivo obliga a abrirlos uno por uno.
Arşiv Ara lee solo el índice de cada archivo comprimido, lo mantiene en memoria y busca en todos a la vez,
como [Everything](https://www.voidtools.com/) busca nombres de archivo en el disco.

## Funciones

| | |
|---|---|
| **Arrastrar y soltar** | Suelta archivos comprimidos o carpetas enteras en la ventana (o sobre el exe). La lista se guarda para la próxima vez. |
| **Búsqueda instantánea** | 1 millón de nombres en 2–20 ms. Cada pulsación actualiza los resultados. |
| **Coincidencia tolerante** | Ignora mayúsculas y acentos: `sirket` encuentra `Şirket`. Los ZIP antiguos con nombres DOS (CP857) se decodifican bien. |
| **Filtros** | `ext:pdf;docx`, `arsiv:2025`, `klasor:` (solo carpetas), `dosya:` (solo archivos). Varias palabras se combinan con Y. |
| **Vista de lista y cuadrícula** | Iconos reales de Windows por tipo. En cuadrícula, las fotos muestran su miniatura. **Ctrl + rueda** hace zoom de 16 a 256 px. |
| **Abrir con un clic** | Doble clic extrae solo ese archivo a una carpeta temporal y lo abre. |
| **Rápido con RAR grandes** | Lee el registro *Quick Open* de WinRAR: una copia de 14 GB con 22.000 archivos se lista en segundos, no en minutos. |
| **Portátil** | Un solo exe de 61 MB, sin instalación ni .NET. La configuración viaja junto al exe en una memoria USB. |
| **Privado** | Funciona totalmente sin conexión. No se envía nada a ningún sitio. |

## Instalación

**PowerShell (una línea)**: descarga la última versión en `%LOCALAPPDATA%\Programs\ArsivAra` y crea un acceso directo en el menú Inicio.

```powershell
irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex
```

**winget**

```powershell
winget install YOUR_GITHUB_USER.ArsivAra
```

**Manual**: descarga `ArsivAra.exe` de la [última versión](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest) y ejecútalo.

## Uso

| Consulta | Significado |
|---|---|
| `factura 2024` | Ambas palabras deben aparecer en la ruta dentro del archivo |
| `ext:pdf;docx` | Solo estas extensiones |
| `arsiv:copia` | Solo archivos comprimidos cuya ruta contiene "copia" |
| `klasor:` / `dosya:` | Solo carpetas / solo archivos |

`Ctrl + rueda` hace zoom, `Ctrl + 0` vuelve a la lista compacta, `Enter` o doble clic abre, `Ctrl + C` copia la ruta, `Esc` borra la búsqueda.

## Por qué es rápido

- **ZIP**: solo se lee el *directorio central* al final del archivo.
- **RAR5**: WinRAR guarda una copia *Quick Open* de las cabeceras al final del archivo. Arşiv Ara la lee de una vez y verifica cada cabecera con CRC32 ([especificación](https://www.rarlab.com/technote.htm)).
- **Búsqueda**: todas las rutas se normalizan en un único búfer UTF-8 que se recorre en paralelo con `IndexOf` vectorizado.

## Compilar desde el código

Requiere el [SDK de .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) en Windows.

```powershell
git clone https://github.com/YOUR_GITHUB_USER/ArsivAra.git
cd ArsivAra
dotnet run --project src/ArsivAra.App -c Release
```

## Licencia

[MIT](LICENSE) © 2026 MaledictusMaggot. Usa [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT).

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Logo de Arşiv Ara"><br>
<sub>Created By <b>MaledictusMaggot®</b></sub>

</div>
