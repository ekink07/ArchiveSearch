<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Icono de Arşiv Ara">

# Arşiv Ara

**Busca dentro de archivos ZIP, RAR, 7z, TAR e ISO al instante, sin descomprimirlos.**

Arrastra tus archivos comprimidos (o deja que los encuentre todos), escribe unas letras y ve cada archivo que coincide en milisegundos.

[English](README.md) · [Türkçe](README.tr.md) · [Deutsch](README.de.md) · **Español**

[![Release](https://img.shields.io/github/v/release/YOUR_GITHUB_USER/ArsivAra?color=F7861F&label=versi%C3%B3n)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest)
[![CI](https://github.com/YOUR_GITHUB_USER/ArsivAra/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_GITHUB_USER/ArsivAra/actions/workflows/ci.yml)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![x64 | ARM64](https://img.shields.io/badge/x64%20%7C%20ARM64-0B1B2E)
[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-0B1B2E)](#licencia)

<img src="docs/images/search.png" width="860" alt="La búsqueda 'toplanti' encuentra notas de reunión en dos copias de seguridad, una dentro de un archivo anidado; la vista previa muestra el texto y las dos versiones">

</div>

## Por qué

Las copias de seguridad, carpetas de proyectos y descargas acaban convertidas en decenas de archivos comprimidos grandes. Encontrar un solo archivo obliga a abrirlos uno por uno.
Arşiv Ara lee solo el índice de cada archivo comprimido, lo mantiene en memoria y busca en todos a la vez,
como [Everything](https://www.voidtools.com/) busca nombres de archivo en el disco.

## Funciones

| | |
|---|---|
| **Arrastrar o discos enteros** | Suelta archivos comprimidos o carpetas en la ventana (o sobre el exe), o deja que encuentre todos los del equipo. Los nuevos y modificados se detectan al momento. |
| **Búsqueda instantánea** | 1 millón de nombres en 2–20 ms. Cada pulsación actualiza los resultados. |
| **Lenguaje de búsqueda** | O `a\|b`, NO `!borrador`, `"frase exacta"`, comodines `*.pdf`, `size:`, `date:`, `regex:`, `dupe:`, `content:`. Guarda búsquedas con `Ctrl + S`. |
| **Coincidencia tolerante** | Ignora mayúsculas y acentos: `sirket` encuentra `Şirket`. Los ZIP antiguos con nombres DOS (CP857) se decodifican bien. |
| **Formatos** | ZIP, RAR (también por partes, RAR4/RAR5), 7z, TAR (`.gz`, `.bz2`, `.xz`, `.zst`, `.tgz`), ISO (ISO 9660, Joliet, Rock Ridge, UDF). También indexa archivos dentro de archivos. |
| **Archivos cifrados** | RAR y 7z con nombres cifrados: doble clic, escribe la contraseña y, si quieres, se recuerda con DPAPI de Windows. Los ZIP con AES se listan sin contraseña. |
| **Vista previa** | `Alt + P`: imágenes, archivos de texto y texto de Word, Excel, PowerPoint y OpenDocument, además de las demás versiones del mismo archivo. |
| **Versiones y duplicados** | Clic derecho › *Versiones* muestra qué copia tiene la versión más reciente; `dupe:` encuentra archivos idénticos por tamaño y CRC. |
| **Extraer y arrastrar** | `Ctrl + E` extrae la selección a una carpeta; arrastra resultados directamente al Explorador. |
| **Lista, cuadrícula, orden** | Iconos de Windows, miniaturas de fotos, `Ctrl + rueda` de 16 a 256 px. Haz clic en un encabezado para ordenar. |
| **Seguro** | Los archivos extraídos conservan la marca de la Web del comprimido; programas y scripts piden confirmación antes de abrirse. Totalmente sin conexión. |
| **Portátil, 4 idiomas** | Un solo exe para x64 o ARM64, sin instalación ni .NET. Türkçe, English, Deutsch, Español. |

## Instalación

**PowerShell (una línea)**: elige la versión x64 o ARM64, verifica su SHA256, la instala en `%LOCALAPPDATA%\Programs\ArsivAra`
y la añade al menú Inicio y a *Configuración › Aplicaciones* (desde ahí se desinstala). No necesita permisos de administrador.

```powershell
irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex
```

Opciones: `-Desktop` (acceso directo en el escritorio), `-Uninstall`, `-Uninstall -RemoveData` (borra también configuración y biblioteca):

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1))) -Desktop
```

**winget**

```powershell
winget install VerloraStudios.ArsivAra
```

**Manual**: descarga `ArsivAra.exe` (x64) o `ArsivAra-arm64.exe` de la [última versión](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest) y ejecútalo.
Con un `portable.txt` vacío junto al exe (o en una memoria USB), la configuración y la biblioteca se guardan en la carpeta `ArsivAra-veri` a su lado.

## Uso

| Consulta | Encuentra |
|---|---|
| `factura 2024` | Ambas palabras en la ruta dentro del archivo (Y) |
| `informe\|diapositivas` | Cualquiera de las dos (O) |
| `!borrador` | Todo lo que no la contiene (NO) |
| `"extracto bancario"` | Frase exacta, con espacios |
| `*.pdf`, `IMG_00??.jpg` | Comodines en el nombre del archivo |
| `ext:pdf;docx` | Solo estas extensiones |
| `archive:copia` | Solo archivos cuyo nombre o ruta contiene "copia" |
| `folder:` / `file:` | Solo carpetas / solo archivos |
| `name:="Informe.pdf"` | Nombre de archivo exacto |
| `size:>10mb`, `size:1mb..1gb` | Tamaño |
| `date:2024`, `date:>=2024-06-01`, `date:thisweek` | Fecha de modificación |
| `regex:^factura_\d+` | Expresión regular |
| `dupe:` | Copias idénticas (tamaño y CRC) |
| `content:esmeralda` | Dentro de archivos de texto y Office; lento, acótalo con otras condiciones |

El botón **?** junto al cuadro de búsqueda muestra la misma lista. Teclas: `Ctrl + F` buscar, `Enter`/doble clic abrir, `Ctrl + E` extraer,
`Ctrl + C` copiar la ruta, `Alt + P` vista previa, `Ctrl + S` guardar búsqueda, `Ctrl + rueda` zoom, `Ctrl + 0` lista compacta,
`Esc` borrar la búsqueda, `Ctrl + O` añadir archivos, `Ctrl + ,` configuración, `F1` acerca de y licencia.

## Por qué es rápido

- **ZIP**: solo se lee el *directorio central* al final del archivo.
- **RAR5**: WinRAR guarda una copia *Quick Open* de las cabeceras al final del archivo. Arşiv Ara la lee de una vez y verifica cada cabecera con CRC32 ([especificación](https://www.rarlab.com/technote.htm)).
- **ISO**: los directorios UDF e ISO 9660 se leen directamente, sin montar la imagen.
- **Búsqueda**: todas las rutas se normalizan en un único búfer UTF-8 que se recorre en paralelo con `IndexOf` vectorizado.
- **Discos enteros**: en NTFS los archivos se encuentran a través de la MFT (un pequeño proceso auxiliar con permisos de administrador; la ventana no los necesita) y se mantienen al día con el diario USN.

## Compilar desde el código

Requiere el [SDK de .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0) en Windows.

```powershell
git clone https://github.com/YOUR_GITHUB_USER/ArsivAra.git
cd ArsivAra
dotnet run --project src/ArsivAra.App -c Release
dotnet test tests/ArsivAra.Core.Tests        # pruebas unitarias con archivos de ejemplo reales
pwsh tests/ui/ui-smoke.ps1                   # prueba de la interfaz con UI Automation (compilación Release)
```

Una etiqueta como `v1.1.0` ejecuta las pruebas, compila los exe x64 y ARM64, los firma si hay secretos configurados ([packaging/signing](packaging/signing/README.md)),
publica una versión en GitHub y actualiza [winget](packaging/winget/README.md).

## Limitaciones

- Los archivos con nombres cifrados necesitan la contraseña para listarse.
- Los archivos anidados se leen un nivel de profundidad, hasta 64 por archivo y 256 MB cada uno.
- `content:` solo lee texto y archivos de Office (no PDF) y puede ser lento en archivos sólidos grandes.

## Licencia

Arşiv Ara es gratuito y de código abierto con la [licencia MIT](LICENSE): puedes usarlo, copiarlo, modificarlo, fusionarlo, publicarlo,
distribuirlo, sublicenciarlo y venderlo, siempre que se conserven el aviso de copyright y el texto de la licencia. Se ofrece sin garantía.

| | |
|---|---|
| **Fabricante** | Verlora Studios |
| **Creado por** | MaledictusMaggot |
| **Copyright** | © 2026 MaledictusMaggot |
| **Terceros** | [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.4 como código fuente en [src/ThirdParty](src/ThirdParty/README.md) (MIT; incluye código con licencias UnRAR, Ms-PL, Apache 2.0, BSD y zlib) y, en la versión portátil, el runtime de .NET (MIT). Todos los textos: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). |

En el programa: **Acerca de › Texto de la licencia** (`F1`); `ArsivAra.Cli lisans` lo muestra en la consola.

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Logo de Arşiv Ara"><br>
<sub>Verlora Studios · Created By <b>MaledictusMaggot®</b></sub>

</div>
