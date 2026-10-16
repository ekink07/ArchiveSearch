<div align="center">

<img src="docs/images/icon-256.png" width="112" alt="Arşiv Ara Symbol">

# Arşiv Ara

**Sofortige Suche in ZIP-, RAR-, 7z- und TAR-Archiven, ohne sie zu entpacken.**

Archive hineinziehen, ein paar Buchstaben tippen und jede passende Datei in Millisekunden sehen.

[English](README.md) · [Türkçe](README.tr.md) · **Deutsch** · [Español](README.es.md)

[![Release](https://img.shields.io/github/v/release/YOUR_GITHUB_USER/ArsivAra?color=F7861F&label=release)](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)
[![Lizenz: MIT](https://img.shields.io/badge/Lizenz-MIT-0B1B2E)](LICENSE)

<img src="docs/images/grid.png" width="860" alt="Kachelansicht mit Foto-Vorschaubildern und Windows-Dateisymbolen">

</div>

## Warum

Backups, Projektordner und Downloads werden zu Dutzenden großer Archive. Um eine einzelne Datei zu finden, muss man sie nacheinander öffnen.
Arşiv Ara liest nur das Inhaltsverzeichnis der Archive, hält es im Speicher und durchsucht alle gleichzeitig,
so wie [Everything](https://www.voidtools.com/) Dateinamen auf der Festplatte durchsucht.

## Funktionen

| | |
|---|---|
| **Drag & Drop** | Archive oder ganze Ordner auf das Fenster (oder die exe) ziehen. Die Liste bleibt für den nächsten Start erhalten. |
| **Sofortsuche** | 1 Million Dateinamen in 2–20 ms. Jeder Tastendruck aktualisiert die Ergebnisse. |
| **Tolerante Suche** | Groß-/Kleinschreibung und Akzente werden ignoriert: `sirket` findet `Şirket`. Alte ZIPs mit DOS-Dateinamen (CP857) werden richtig dekodiert. |
| **Filter** | `ext:pdf;docx`, `arsiv:2025`, `klasor:` (nur Ordner), `dosya:` (nur Dateien). Mehrere Wörter werden mit UND verknüpft. |
| **Listen- und Kachelansicht** | Echte Windows-Symbole pro Dateityp. In der Kachelansicht zeigen Fotos ihr Vorschaubild. **Strg + Mausrad** zoomt von 16 bis 256 px. |
| **Mit einem Klick öffnen** | Doppelklick entpackt nur diese eine Datei in einen temporären Ordner und öffnet sie. |
| **Schnell bei großen RARs** | Liest den *Quick Open*-Eintrag von WinRAR: ein 14-GB-Backup mit 22.000 Dateien wird in Sekunden statt Minuten aufgelistet. |
| **Portabel** | Eine einzige 61-MB-exe, keine Installation, kein .NET nötig. Die Einstellungen liegen auf dem USB-Stick neben der exe. |
| **Privat** | Funktioniert komplett offline. Nichts wird hochgeladen. |

## Installation

**PowerShell (eine Zeile)**: lädt die neueste Version nach `%LOCALAPPDATA%\Programs\ArsivAra` und legt eine Startmenü-Verknüpfung an.

```powershell
irm https://raw.githubusercontent.com/YOUR_GITHUB_USER/ArsivAra/main/install.ps1 | iex
```

**winget**

```powershell
winget install YOUR_GITHUB_USER.ArsivAra
```

**Manuell**: `ArsivAra.exe` aus dem [neuesten Release](https://github.com/YOUR_GITHUB_USER/ArsivAra/releases/latest) herunterladen und starten.

## Bedienung

| Abfrage | Bedeutung |
|---|---|
| `rechnung 2024` | Beide Wörter müssen im Pfad innerhalb des Archivs vorkommen |
| `ext:pdf;docx` | Nur diese Endungen |
| `arsiv:backup` | Nur Archive, deren Pfad „backup“ enthält |
| `klasor:` / `dosya:` | Nur Ordner / nur Dateien |

`Strg + Mausrad` zoomt, `Strg + 0` kehrt zur kompakten Liste zurück, `Enter` oder Doppelklick öffnet, `Strg + C` kopiert den Pfad, `Esc` leert die Suche.

## Warum es schnell ist

- **ZIP**: Nur das *zentrale Verzeichnis* am Dateiende wird gelesen.
- **RAR5**: WinRAR schreibt eine *Quick Open*-Kopie aller Dateiköpfe ans Ende des Archivs. Arşiv Ara liest diesen Block auf einmal und prüft jeden Kopf per CRC32 ([Formatbeschreibung](https://www.rarlab.com/technote.htm)).
- **Suche**: Alle Pfade werden normalisiert in einen UTF-8-Puffer geschrieben, der parallel mit vektorisiertem `IndexOf` durchsucht wird.

## Aus dem Quellcode bauen

Benötigt das [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) unter Windows.

```powershell
git clone https://github.com/YOUR_GITHUB_USER/ArsivAra.git
cd ArsivAra
dotnet run --project src/ArsivAra.App -c Release
```

## Lizenz

[MIT](LICENSE) © 2026 MaledictusMaggot. Verwendet [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT).

---

<div align="center">

<img src="docs/images/icon-64.png" width="40" alt="Arşiv Ara Logo"><br>
<sub>Created By <b>MaledictusMaggot®</b></sub>

</div>
