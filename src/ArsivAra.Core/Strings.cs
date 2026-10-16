using System.Globalization;

namespace ArsivAra.Core;

/// <summary>
/// UI texts: Turkish, English, German, Spanish. Each key carries all four languages together, so no translation can be missing;
/// tests check that the format placeholders ({0}, {1}) are the same in every language.
/// </summary>
public static class Strings
{
    /// <summary>Supported UI languages; English is the default and comes first.</summary>
    public static readonly string[] Languages = ["en", "tr", "de", "es"];

    public const string DefaultLanguage = "en";

    /// <summary>Settings value meaning "follow the Windows display language".</summary>
    public const string SystemLanguage = "system";

    /// <summary>Each language in its own name (for the language picker; never translated).</summary>
    public static readonly IReadOnlyDictionary<string, string> NativeNames = new Dictionary<string, string>
    {
        ["en"] = "English", ["tr"] = "Türkçe", ["de"] = "Deutsch", ["es"] = "Español",
    };

    /// <summary>
    /// The language to show: the one chosen in the settings; for <see cref="SystemLanguage"/> the Windows display
    /// language when it is supported; otherwise (first start, unknown value) English.
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (configured is { Length: > 0 } && Languages.Contains(configured)) return configured;
        if (configured == SystemLanguage)
        {
            string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (Languages.Contains(system)) return system;
        }
        return DefaultLanguage;
    }

    /// <summary>Culture for number and date formatting.</summary>
    public static CultureInfo CultureOf(string language) => CultureInfo.GetCultureInfo(language switch
    {
        "tr" => "tr-TR",
        "de" => "de-DE",
        "es" => "es-ES",
        _ => "en-US",
    });

    public static string Get(string language, string key)
    {
        if (!Table.TryGetValue(key, out var row)) return key;
        return language switch { "tr" => row.Tr, "de" => row.De, "es" => row.Es, _ => row.En };
    }

    public static IEnumerable<string> Keys => Table.Keys;

    internal static readonly IReadOnlyDictionary<string, (string Tr, string En, string De, string Es)> Table = Build(
    [
        // ---- General ----
        ("OK", "Tamam", "OK", "OK", "Aceptar"),
        ("Cancel", "Vazgeç", "Cancel", "Abbrechen", "Cancelar"),
        ("Close", "Kapat", "Close", "Schließen", "Cerrar"),

        // ---- Top bar ----
        ("SearchHint", "Arşivlerin içinde ara…", "Search inside archives…", "In Archiven suchen …", "Buscar dentro de los archivos…"),
        ("SearchHelp", "Arama dili", "Search syntax", "Suchsyntax", "Sintaxis de búsqueda"),
        ("SearchHelpText",
            "kelime1 kelime2 — hepsi geçmeli (VE)\na|b — biri geçmeli (VEYA)\n!kelime — geçmemeli (DEĞİL)\n\"tam ifade\" — boşluklu ifade\n*.pdf  rapor?.docx — joker\next:pdf;docx — uzantı\nad:rapor — sadece dosya adında;  ad:=\"Tam Ad.pdf\"\nklasor: / dosya: — sadece klasörler / dosyalar\narsiv:yedek — arşivin yolunda geçmeli\nboyut:>10mb  boyut:1mb..1gb\ntarih:2024  tarih:>=2024-06-01  tarih:bugun\nregex:^fatura_\\d+ — düzenli ifade\nkopya: — aynı ad ve boyutta başka dosyası olanlar\nicerik:\"metin\" — dosyaların içinde (metin ve Office belgeleri)",
            "word1 word2 — all must match (AND)\na|b — either one (OR)\n!word — must not match (NOT)\n\"exact phrase\" — phrase with spaces\n*.pdf  report?.docx — wildcards\next:pdf;docx — extension\nname:report — file name only;  name:=\"Exact Name.pdf\"\nfolder: / file: — only folders / files\narchive:backup — archive path contains\nsize:>10mb  size:1mb..1gb\ndate:2024  date:>=2024-06-01  date:today\nregex:^invoice_\\d+ — regular expression\ndupe: — files that have a same-name, same-size copy\ncontent:\"text\" — inside files (text and Office documents)",
            "wort1 wort2 — alle müssen vorkommen (UND)\na|b — eines davon (ODER)\n!wort — darf nicht vorkommen (NICHT)\n\"genauer Ausdruck\" — Ausdruck mit Leerzeichen\n*.pdf  bericht?.docx — Platzhalter\next:pdf;docx — Dateiendung\nname:bericht — nur im Dateinamen;  name:=\"Genauer Name.pdf\"\nfolder: / file: — nur Ordner / Dateien\narchive:backup — Archivpfad enthält\nsize:>10mb  size:1mb..1gb\ndate:2024  date:>=2024-06-01  date:today\nregex:^rechnung_\\d+ — regulärer Ausdruck\ndupe: — Dateien mit gleichnamiger, gleich großer Kopie\ncontent:\"Text\" — im Dateiinhalt (Text- und Office-Dokumente)",
            "palabra1 palabra2 — todas deben aparecer (Y)\na|b — cualquiera (O)\n!palabra — no debe aparecer (NO)\n\"frase exacta\" — frase con espacios\n*.pdf  informe?.docx — comodines\next:pdf;docx — extensión\nname:informe — solo en el nombre;  name:=\"Nombre Exacto.pdf\"\nfolder: / file: — solo carpetas / archivos\narchive:copia — la ruta del archivo comprimido contiene\nsize:>10mb  size:1mb..1gb\ndate:2024  date:>=2024-06-01  date:today\nregex:^factura_\\d+ — expresión regular\ndupe: — archivos con una copia del mismo nombre y tamaño\ncontent:\"texto\" — dentro de los archivos (texto y documentos de Office)"),
        ("SaveSearch", "Aramayı kaydet (Ctrl+S)", "Save this search (Ctrl+S)", "Suche speichern (Strg+S)", "Guardar esta búsqueda (Ctrl+S)"),
        ("ViewList", "Liste görünümü (Ctrl+tekerlek ile simge boyutu)", "List view (Ctrl+wheel changes the icon size)", "Listenansicht (Strg+Mausrad ändert die Symbolgröße)", "Vista de lista (Ctrl+rueda cambia el tamaño de los iconos)"),
        ("ViewGrid", "Kareli görünüm (Ctrl+tekerlek ile büyüt/küçült)", "Grid view (Ctrl+wheel to zoom)", "Kachelansicht (Strg+Mausrad zum Zoomen)", "Vista de cuadrícula (Ctrl+rueda para hacer zoom)"),
        ("Preview", "Önizleme bölmesi (Alt+P)", "Preview pane (Alt+P)", "Vorschaubereich (Alt+P)", "Panel de vista previa (Alt+P)"),
        ("SettingsTip", "Ayarlar (Ctrl+,)", "Settings (Ctrl+,)", "Einstellungen (Strg+,)", "Configuración (Ctrl+,)"),
        ("AddArchives", "Arşiv ekle", "Add archives", "Archive hinzufügen", "Añadir archivos"),
        ("AddArchivesTip", "RAR, ZIP, 7z, TAR ve ISO dosyalarını ekle (Ctrl+O)", "Add RAR, ZIP, 7z, TAR and ISO files (Ctrl+O)", "RAR-, ZIP-, 7z-, TAR- und ISO-Dateien hinzufügen (Strg+O)", "Añadir archivos RAR, ZIP, 7z, TAR e ISO (Ctrl+O)"),
        ("AddFolder", "Klasör ekle", "Add folder", "Ordner hinzufügen", "Añadir carpeta"),
        ("AddFolderTip", "Bir klasördeki tüm arşivleri ekle", "Add every archive in a folder", "Alle Archive eines Ordners hinzufügen", "Añadir todos los archivos comprimidos de una carpeta"),

        // ---- Sidebar ----
        ("Archives", "Arşivler", "Archives", "Archive", "Archivos"),
        ("ArchivesCount", "Arşivler ({0})", "Archives ({0})", "Archive ({0})", "Archivos ({0})"),
        ("SavedSearches", "Kayıtlı aramalar", "Saved searches", "Gespeicherte Suchen", "Búsquedas guardadas"),
        ("RemoveFromList", "Listeden kaldır", "Remove from list", "Aus der Liste entfernen", "Quitar de la lista"),
        ("RemoveNamed", "{0} listeden kaldır", "Remove {0} from the list", "{0} aus der Liste entfernen", "Quitar {0} de la lista"),
        ("DeleteSavedSearch", "Kayıtlı aramayı sil", "Delete saved search", "Gespeicherte Suche löschen", "Eliminar búsqueda guardada"),
        ("ArchiveFiles", "{0} dosya · {1}", "{0} files · {1}", "{0} Dateien · {1}", "{0} archivos · {1}"),
        ("ArchiveNested", "{0} iç arşiv", "{0} nested", "{0} verschachtelt", "{0} anidados"),
        ("ArchiveScanned", "disk taraması", "disk scan", "Laufwerkssuche", "análisis de disco"),
        ("ArchiveMissing", "Dosya bulunamadı (taşınmış ya da silinmiş)", "File not found (moved or deleted)", "Datei nicht gefunden (verschoben oder gelöscht)", "Archivo no encontrado (movido o eliminado)"),
        ("ArchiveEncrypted", "Şifreli: parola girmek için çift tıklayın", "Encrypted: double-click to enter the password", "Verschlüsselt: Doppelklick, um das Kennwort einzugeben", "Cifrado: doble clic para introducir la contraseña"),
        ("ArchiveAccessDenied", "Erişim engellendi", "Access denied", "Zugriff verweigert", "Acceso denegado"),
        ("ArchiveInUse", "Başka bir program tarafından kullanılıyor", "In use by another program", "Wird von einem anderen Programm verwendet", "En uso por otro programa"),
        ("ArchiveUnreadable", "Okunamadı: {0}", "Could not be read: {0}", "Nicht lesbar: {0}", "No se pudo leer: {0}"),

        // ---- Columns and sorting ----
        ("ColName", "Ad", "Name", "Name", "Nombre"),
        ("ColFolder", "Arşiv içindeki klasör", "Folder in archive", "Ordner im Archiv", "Carpeta en el archivo"),
        ("ColSize", "Boyut", "Size", "Größe", "Tamaño"),
        ("ColModified", "Değiştirilme", "Modified", "Geändert", "Modificado"),
        ("ColArchive", "Arşiv", "Archive", "Archiv", "Archivo"),
        ("ColType", "Tür", "Type", "Typ", "Tipo"),
        ("SortBy", "Sırala", "Sort by", "Sortieren nach", "Ordenar por"),
        ("SortDescending", "Azalan", "Descending", "Absteigend", "Descendente"),
        ("SortTip", "Sıralamak için tıklayın", "Click to sort", "Zum Sortieren klicken", "Haz clic para ordenar"),

        // ---- Menus ----
        ("MenuOpen", "Aç", "Open", "Öffnen", "Abrir"),
        ("MenuShowArchive", "Arşivi klasörde göster", "Show archive in folder", "Archiv im Ordner anzeigen", "Mostrar el archivo en la carpeta"),
        ("MenuExtract", "Seçilenleri çıkar…", "Extract selected…", "Auswahl entpacken …", "Extraer selección…"),
        ("MenuVersions", "Tüm sürümlerini göster", "Show all versions", "Alle Versionen anzeigen", "Mostrar todas las versiones"),
        ("MenuCopyPath", "Yolu kopyala", "Copy path", "Pfad kopieren", "Copiar ruta"),
        ("MenuEnterPassword", "Parola gir…", "Enter password…", "Kennwort eingeben …", "Introducir contraseña…"),
        ("MenuReread", "Yeniden oku", "Read again", "Neu einlesen", "Volver a leer"),
        ("MenuShowInFolder", "Klasörde göster", "Show in folder", "Im Ordner anzeigen", "Mostrar en la carpeta"),

        // ---- Empty screen ----
        ("EmptyTitle", "Arşivleri buraya sürükleyip bırakın", "Drag and drop archives here", "Archive hierher ziehen", "Arrastra los archivos aquí"),
        ("EmptyText", "RAR, ZIP, 7z, TAR ve ISO dosyalarının içindeki her dosya adı aranabilir hâle gelir. Bir klasör bırakırsanız içindeki tüm arşivler eklenir.",
            "Every file name inside RAR, ZIP, 7z, TAR and ISO files becomes searchable. Drop a folder to add every archive in it.",
            "Jeder Dateiname in RAR-, ZIP-, 7z-, TAR- und ISO-Dateien wird durchsuchbar. Ein Ordner fügt alle darin enthaltenen Archive hinzu.",
            "Cada nombre de archivo dentro de los RAR, ZIP, 7z, TAR e ISO se puede buscar. Suelta una carpeta para añadir todos sus archivos comprimidos."),
        ("EmptyPickFiles", "Dosya seç", "Choose files", "Dateien wählen", "Elegir archivos"),
        ("EmptyPickFolder", "Klasör seç", "Choose folder", "Ordner wählen", "Elegir carpeta"),
        ("EmptyScanDisks", "Tüm diskleri tara", "Scan all disks", "Alle Laufwerke durchsuchen", "Analizar todos los discos"),
        ("DropHere", "Bırakın, eklensin", "Drop to add", "Zum Hinzufügen loslassen", "Suelta para añadir"),
        ("AboutTip", "Arşiv Ara hakkında (F1)", "About Arşiv Ara (F1)", "Über Arşiv Ara (F1)", "Acerca de Arşiv Ara (F1)"),

        // ---- Status bar ----
        ("StatusNoArchives", "Henüz arşiv eklenmedi", "No archives added yet", "Noch keine Archive hinzugefügt", "Aún no hay archivos añadidos"),
        ("StatusLibrary", "{0} arşiv · {1} dosya aranabilir", "{0} archives · {1} files searchable", "{0} Archive · {1} Dateien durchsuchbar", "{0} archivos comprimidos · {1} archivos para buscar"),
        ("StatusResults", "{0} sonuç ({1} ms)", "{0} results ({1} ms)", "{0} Treffer ({1} ms)", "{0} resultados ({1} ms)"),
        ("StatusScope", "sadece {0}", "only {0}", "nur {0}", "solo {0}"),
        ("StatusIconSize", "Simge boyutu: {0} px · {1} sonuç", "Icon size: {0} px · {1} results", "Symbolgröße: {0} px · {1} Treffer", "Tamaño de icono: {0} px · {1} resultados"),
        ("StatusSearchingFolder", "Klasörde arşivler aranıyor: {0}", "Looking for archives in {0}", "Suche Archive in {0}", "Buscando archivos en {0}"),
        ("StatusReading", "Okunuyor: {0}", "Reading {0}", "Lese {0}", "Leyendo {0}"),
        ("StatusRead", "Okundu: {0}", "Read {0}", "Gelesen: {0}", "Leído: {0}"),
        ("StatusLooking", "Aranıyor…", "Searching…", "Suche …", "Buscando…"),
        ("StatusAdded", "{0} arşiv eklendi", "{0} archives added", "{0} Archive hinzugefügt", "{0} archivos añadidos"),
        ("StatusUpdated", "{0} arşiv güncellendi", "{0} archives updated", "{0} Archive aktualisiert", "{0} archivos actualizados"),
        ("StatusFailed", "{0} arşiv okunamadı", "{0} archives could not be read", "{0} Archive nicht lesbar", "{0} archivos no se pudieron leer"),
        ("StatusSkipped", "{0} dosya arşiv değil, atlandı", "{0} files are not archives, skipped", "{0} Dateien sind keine Archive, übersprungen", "{0} archivos no son comprimidos, omitidos"),
        ("StatusNothingAdded", "Eklenecek arşiv bulunamadı", "No archives found to add", "Keine Archive zum Hinzufügen gefunden", "No se encontraron archivos para añadir"),
        ("StatusAddFailed", "Eklenemedi: {0}", "Could not add: {0}", "Hinzufügen fehlgeschlagen: {0}", "No se pudo añadir: {0}"),
        ("StatusOpening", "Açılıyor: {0}…", "Opening {0}…", "Öffne {0} …", "Abriendo {0}…"),
        ("StatusOpened", "Açıldı: {0}", "Opened {0}", "Geöffnet: {0}", "Abierto: {0}"),
        ("OpenFailed", "Dosya açılamadı:\n{0}", "The file could not be opened:\n{0}", "Die Datei konnte nicht geöffnet werden:\n{0}", "No se pudo abrir el archivo:\n{0}"),
        ("StatusExtracting", "Çıkarılıyor: {0} / {1}", "Extracting {0} / {1}", "Entpacke {0} / {1}", "Extrayendo {0} / {1}"),
        ("StatusExtracted", "{0} dosya çıkarıldı: {1}", "{0} files extracted to {1}", "{0} Dateien entpackt nach {1}", "{0} archivos extraídos en {1}"),
        ("StatusExtractFailed", "Çıkarılamadı: {0}", "Extraction failed: {0}", "Entpacken fehlgeschlagen: {0}", "Error al extraer: {0}"),
        ("StatusContent", "İçerik aranıyor: {0} / {1} dosya…", "Searching contents: {0} / {1} files…", "Durchsuche Inhalte: {0} / {1} Dateien …", "Buscando en el contenido: {0} / {1} archivos…"),
        ("StatusContentDone", "{0} sonuç · {1} dosyanın içine bakıldı ({2} ms)", "{0} results · looked inside {1} files ({2} ms)", "{0} Treffer · {1} Dateien durchsucht ({2} ms)", "{0} resultados · se buscó dentro de {1} archivos ({2} ms)"),
        ("StatusContentSkipped", "{0} dosya türü ya da boyutu nedeniyle atlandı", "{0} files skipped (type or size)", "{0} Dateien übersprungen (Typ oder Größe)", "{0} archivos omitidos (tipo o tamaño)"),
        ("StatusVersions", "{0} sürüm · en yenisi: {1}", "{0} versions · newest: {1}", "{0} Versionen · neueste: {1}", "{0} versiones · la más reciente: {1}"),
        ("StatusScanning", "Disk taranıyor: {0}", "Scanning disks: {0}", "Durchsuche Laufwerke: {0}", "Analizando discos: {0}"),
        ("StatusScanProgress", "Disk taranıyor: {0} arşiv bulundu, {1} okundu", "Scanning disks: {0} archives found, {1} read", "Durchsuche Laufwerke: {0} Archive gefunden, {1} gelesen", "Analizando discos: {0} archivos encontrados, {1} leídos"),
        ("StatusScanDone", "Disk taraması bitti: {0} arşiv ({1} sn) · değişiklikler canlı izleniyor", "Disk scan finished: {0} archives ({1} s) · watching for changes", "Laufwerkssuche fertig: {0} Archive ({1} s) · Änderungen werden überwacht", "Análisis terminado: {0} archivos ({1} s) · vigilando los cambios"),
        ("StatusScanFailed", "Disk taraması yapılamadı: {0}", "Disk scan failed: {0}", "Laufwerkssuche fehlgeschlagen: {0}", "Falló el análisis de discos: {0}"),
        ("StatusMft", "MFT okunuyor (yönetici izni)…", "Reading the MFT (administrator permission)…", "Lese die MFT (Administratorrechte) …", "Leyendo la MFT (permiso de administrador)…"),
        ("StatusElevationDeclined", "Yönetici izni verilmedi; klasörler gezilerek taranıyor (daha yavaş)", "Administrator permission was not granted; scanning folders instead (slower)", "Keine Administratorrechte; Ordner werden durchsucht (langsamer)", "No se concedió permiso de administrador; se recorren las carpetas (más lento)"),
        ("StatusLiveChange", "Değişiklik: {0}", "Changed: {0}", "Geändert: {0}", "Cambiado: {0}"),
        ("StatusPreparingDrag", "Sürüklemek için çıkarılıyor…", "Extracting for drag and drop…", "Entpacke für Drag & Drop …", "Extrayendo para arrastrar…"),
        ("StatusIndexMigrated", "Kütüphane yeni biçime dönüştürüldü (yedek: {0})", "Library converted to the new format (backup: {0})", "Bibliothek ins neue Format umgewandelt (Sicherung: {0})", "Biblioteca convertida al nuevo formato (copia: {0})"),
        ("StatusIndexUnreadable", "Kütüphane dosyası okunamadı, kenara alındı: {0}", "The library file could not be read and was set aside: {0}", "Die Bibliotheksdatei war nicht lesbar und wurde beiseitegelegt: {0}", "No se pudo leer el archivo de la biblioteca; se apartó: {0}"),
        ("StatusSearchSaved", "Arama kaydedildi: {0}", "Search saved: {0}", "Suche gespeichert: {0}", "Búsqueda guardada: {0}"),
        ("StatusPasswordWrong", "Parola yanlış: {0}", "Wrong password: {0}", "Falsches Kennwort: {0}", "Contraseña incorrecta: {0}"),
        ("QueryErrorSize", "Geçersiz boyut: {0} (örnek: boyut:>10mb, boyut:1mb..1gb)", "Invalid size: {0} (e.g. size:>10mb, size:1mb..1gb)", "Ungültige Größe: {0} (z. B. size:>10mb, size:1mb..1gb)", "Tamaño no válido: {0} (p. ej. size:>10mb, size:1mb..1gb)"),
        ("QueryErrorDate", "Geçersiz tarih: {0} (örnek: tarih:2024, tarih:>=2024-06-01, tarih:bugun)", "Invalid date: {0} (e.g. date:2024, date:>=2024-06-01, date:today)", "Ungültiges Datum: {0} (z. B. date:2024, date:>=2024-06-01, date:today)", "Fecha no válida: {0} (p. ej. date:2024, date:>=2024-06-01, date:today)"),
        ("QueryErrorRegex", "Geçersiz düzenli ifade: {0}", "Invalid regular expression: {0}", "Ungültiger regulärer Ausdruck: {0}", "Expresión regular no válida: {0}"),

        // ---- Windows ----
        ("DialogAddTitle", "Arşiv ekle", "Add archives", "Archive hinzufügen", "Añadir archivos"),
        ("DialogFilterArchives", "Arşivler", "Archives", "Archive", "Archivos comprimidos"),
        ("DialogFilterAll", "Tüm dosyalar", "All files", "Alle Dateien", "Todos los archivos"),
        ("DialogFolderTitle", "İçindeki arşivler eklenecek klasörü seçin", "Choose a folder to add the archives in it", "Ordner wählen, dessen Archive hinzugefügt werden", "Elige una carpeta para añadir sus archivos comprimidos"),
        ("DialogExtractTitle", "Dosyaların çıkarılacağı klasörü seçin", "Choose where to extract the files", "Zielordner zum Entpacken wählen", "Elige dónde extraer los archivos"),
        ("DialogRootTitle", "Taranacak klasörü seçin", "Choose a folder to scan", "Zu durchsuchenden Ordner wählen", "Elige una carpeta para analizar"),
        ("DialogExcludeTitle", "Taranmayacak klasörü seçin", "Choose a folder to skip", "Auszulassenden Ordner wählen", "Elige una carpeta para omitir"),
        ("RiskyTitle", "Dikkat: program ya da betik", "Warning: program or script", "Achtung: Programm oder Skript", "Atención: programa o script"),
        ("RiskyText", "‘{0}’ çalıştırılabilir bir dosya. Arşivlerden açılan programlar bilgisayarınıza zarar verebilir.\n\nYalnızca kaynağına güveniyorsanız açın. Yine de açılsın mı?",
            "‘{0}’ is an executable file. Programs opened from archives can harm your computer.\n\nOnly open it if you trust where it came from. Open it anyway?",
            "‘{0}’ ist eine ausführbare Datei. Programme aus Archiven können Ihrem Computer schaden.\n\nÖffnen Sie sie nur, wenn Sie der Quelle vertrauen. Trotzdem öffnen?",
            "‘{0}’ es un archivo ejecutable. Los programas abiertos desde archivos comprimidos pueden dañar tu equipo.\n\nÁbrelo solo si confías en su origen. ¿Abrirlo de todos modos?"),
        ("ErrorTitle", "Beklenmeyen hata", "Unexpected error", "Unerwarteter Fehler", "Error inesperado"),
        ("ErrorText", "Beklenmeyen bir hata oluştu ve günlüğe yazıldı:\n{0}\n\nGünlük dosyası: {1}", "An unexpected error occurred and was logged:\n{0}\n\nLog file: {1}", "Ein unerwarteter Fehler ist aufgetreten und wurde protokolliert:\n{0}\n\nProtokolldatei: {1}", "Se produjo un error inesperado y se registró:\n{0}\n\nArchivo de registro: {1}"),
        ("PasswordTitle", "Parola gerekli", "Password required", "Kennwort erforderlich", "Se necesita contraseña"),
        ("PasswordPrompt", "‘{0}’ şifreli. Parolayı girin:", "‘{0}’ is encrypted. Enter the password:", "‘{0}’ ist verschlüsselt. Kennwort eingeben:", "‘{0}’ está cifrado. Introduce la contraseña:"),
        ("PasswordRemember", "Bu parolayı hatırla (Windows hesabınızla şifrelenerek saklanır)", "Remember this password (stored encrypted with your Windows account)", "Kennwort merken (mit Ihrem Windows-Konto verschlüsselt gespeichert)", "Recordar esta contraseña (se guarda cifrada con tu cuenta de Windows)"),
        ("PasswordWrong", "Parola yanlış ya da arşiv bozuk.", "Wrong password or damaged archive.", "Falsches Kennwort oder beschädigtes Archiv.", "Contraseña incorrecta o archivo dañado."),
        ("SaveSearchTitle", "Aramayı kaydet", "Save search", "Suche speichern", "Guardar búsqueda"),
        ("SaveSearchPrompt", "Bu arama hangi adla kaydedilsin?", "Name for this search:", "Name für diese Suche:", "Nombre para esta búsqueda:"),

        // ---- Preview ----
        ("PreviewTitle", "Önizleme", "Preview", "Vorschau", "Vista previa"),
        ("PreviewSelect", "Önizlemek için bir dosya seçin", "Select a file to preview it", "Datei für die Vorschau auswählen", "Selecciona un archivo para verlo"),
        ("PreviewNone", "Bu dosya türü için önizleme yok", "No preview for this file type", "Keine Vorschau für diesen Dateityp", "No hay vista previa para este tipo de archivo"),
        ("PreviewSolid", "Bu bir solid arşiv: önizleme için arşivin baştan açılması gerekir.", "This is a solid archive: previewing needs the archive to be unpacked from the start.", "Dies ist ein solides Archiv: Für die Vorschau muss es von Anfang an entpackt werden.", "Es un archivo sólido: la vista previa requiere descomprimirlo desde el principio."),
        ("PreviewLoadAnyway", "Yine de yükle", "Load anyway", "Trotzdem laden", "Cargar de todos modos"),
        ("PreviewTooLarge", "Dosya önizleme için çok büyük ({0})", "File too large to preview ({0})", "Datei zu groß für die Vorschau ({0})", "Archivo demasiado grande para la vista previa ({0})"),
        ("PreviewLoading", "Yükleniyor…", "Loading…", "Wird geladen …", "Cargando…"),
        ("PreviewFailed", "Önizleme yüklenemedi: {0}", "Preview could not be loaded: {0}", "Vorschau konnte nicht geladen werden: {0}", "No se pudo cargar la vista previa: {0}"),
        ("PreviewFolder", "Klasör · {0} öğe", "Folder · {0} items", "Ordner · {0} Elemente", "Carpeta · {0} elementos"),
        ("PreviewVersions", "Sürümler (en yeni önce)", "Versions (newest first)", "Versionen (neueste zuerst)", "Versiones (la más reciente primero)"),
        ("PreviewNoOtherVersions", "Başka sürümü yok", "No other versions", "Keine weiteren Versionen", "No hay otras versiones"),
        ("PreviewEncrypted", "Şifreli içerik: önizleme için parola gerekir", "Encrypted content: a password is needed to preview it", "Verschlüsselter Inhalt: Für die Vorschau ist ein Kennwort nötig", "Contenido cifrado: se necesita contraseña para la vista previa"),
        ("PreviewExtract", "Çıkar…", "Extract…", "Entpacken …", "Extraer…"),

        // ---- Settings ----
        ("SettingsTitle", "Ayarlar", "Settings", "Einstellungen", "Configuración"),
        ("SetLanguage", "Dil", "Language", "Sprache", "Idioma"),
        ("LangSystem", "Windows'un dili", "Windows language", "Windows-Sprache", "Idioma de Windows"),
        ("SetScanHeader", "Disk taraması", "Disk scan", "Laufwerkssuche", "Análisis de discos"),
        ("SetScanDisks", "Bilgisayardaki tüm arşivleri bul ve değişiklikleri canlı izle", "Find every archive on this computer and watch for changes", "Alle Archive auf diesem Computer finden und Änderungen überwachen", "Encontrar todos los archivos comprimidos del equipo y vigilar los cambios"),
        ("SetFastScan", "Hızlı tarama: MFT ile (yönetici izni ister)", "Fast scan using the MFT (asks for administrator permission)", "Schnelle Suche über die MFT (fragt nach Administratorrechten)", "Análisis rápido con la MFT (pide permiso de administrador)"),
        ("SetRoots", "Taranacak klasörler (boşsa tüm sabit diskler)", "Folders to scan (empty: all fixed disks)", "Zu durchsuchende Ordner (leer: alle festen Laufwerke)", "Carpetas para analizar (vacío: todos los discos fijos)"),
        ("SetExcludes", "Taranmayacak klasörler ve listeden kaldırılan arşivler", "Skipped folders and archives removed from the list", "Ausgelassene Ordner und aus der Liste entfernte Archive", "Carpetas omitidas y archivos quitados de la lista"),
        ("SetAdd", "Ekle…", "Add…", "Hinzufügen …", "Añadir…"),
        ("SetRemove", "Kaldır", "Remove", "Entfernen", "Quitar"),
        ("SetScanNow", "Şimdi tara", "Scan now", "Jetzt durchsuchen", "Analizar ahora"),
        ("SetReadNested", "Arşivlerin içindeki arşivleri de oku (zip içinde zip)", "Also read archives inside archives (zip in zip)", "Auch Archive in Archiven lesen (ZIP in ZIP)", "Leer también archivos dentro de archivos (zip dentro de zip)"),
        ("SetPrivacyHeader", "Geçici dosyalar ve gizlilik", "Temporary files and privacy", "Temporäre Dateien und Datenschutz", "Archivos temporales y privacidad"),
        ("SetTempKeep", "Açmak için çıkarılan dosyaları sakla:", "Keep files extracted for opening:", "Zum Öffnen entpackte Dateien behalten:", "Conservar los archivos extraídos para abrir:"),
        ("Days", "{0} gün", "{0} days", "{0} Tage", "{0} días"),
        ("Day1", "1 gün", "1 day", "1 Tag", "1 día"),
        ("SetTempSize", "Şu an: {0}", "Currently: {0}", "Derzeit: {0}", "Ahora: {0}"),
        ("SetTempClean", "Şimdi temizle", "Clean now", "Jetzt bereinigen", "Limpiar ahora"),
        ("SetPasswords", "Kayıtlı parolalar: {0}", "Saved passwords: {0}", "Gespeicherte Kennwörter: {0}", "Contraseñas guardadas: {0}"),
        ("SetPasswordsClear", "Hepsini sil", "Delete all", "Alle löschen", "Eliminar todas"),
        ("SetDataDir", "Veri klasörü: {0}", "Data folder: {0}", "Datenordner: {0}", "Carpeta de datos: {0}"),
        ("SetPortable", "taşınabilir", "portable", "portabel", "portátil"),
        ("SetOpenLogs", "Günlük klasörünü aç", "Open log folder", "Protokollordner öffnen", "Abrir carpeta de registros"),
        ("SetAboutLicense", "Hakkında ve lisans", "About and license", "Info und Lizenz", "Acerca de y licencia"),

        // ---- About ----
        ("AboutTitle", "Arşiv Ara hakkında", "About Arşiv Ara", "Über Arşiv Ara", "Acerca de Arşiv Ara"),
        ("AboutVersion", "Sürüm {0}", "Version {0}", "Version {0}", "Versión {0}"),
        ("AboutTagline", "ZIP, RAR, 7z, TAR ve ISO arşivlerinin içinde,\naçmadan anında arama.", "Search inside ZIP, RAR, 7z, TAR and ISO archives\ninstantly, without extracting them.", "Sofortige Suche in ZIP-, RAR-, 7z-, TAR- und ISO-Archiven,\nohne sie zu entpacken.", "Busca al instante dentro de archivos ZIP, RAR, 7z, TAR e ISO,\nsin descomprimirlos."),
        ("AboutManufacturer", "Üretici: {0}", "Publisher: {0}", "Hersteller: {0}", "Fabricante: {0}"),

        // ---- License ----
        ("License", "Lisans", "License", "Lizenz", "Licencia"),
        ("LicenseTitle", "Arşiv Ara lisansı", "Arşiv Ara license", "Lizenz von Arşiv Ara", "Licencia de Arşiv Ara"),
        ("LicenseKind", "{0} lisansı · açık kaynak", "{0} license · open source", "{0}-Lizenz · Open Source", "Licencia {0} · código abierto"),
        ("LicenseSummary", "Ücretsiz kullanılabilir, değiştirilebilir ve dağıtılabilir; telif ve lisans notunun korunması yeterlidir. Program olduğu gibi, garantisiz sunulur.",
            "Free to use, modify and distribute; just keep the copyright and license notice. The program is provided as is, without warranty.",
            "Kostenlos nutzbar, veränderbar und weitergebbar; Urheber- und Lizenzhinweis müssen erhalten bleiben. Das Programm wird ohne Gewährleistung bereitgestellt.",
            "Se puede usar, modificar y distribuir gratis conservando el aviso de copyright y la licencia. El programa se ofrece tal cual, sin garantía."),
        ("LicenseThirdParty", "Üçüncü taraf: SharpCompress ve içerdiği bileşenler, .NET", "Third party: SharpCompress and the components it includes, .NET", "Drittanbieter: SharpCompress mit den enthaltenen Komponenten, .NET", "Terceros: SharpCompress y los componentes que incluye, .NET"),
        ("LicenseShow", "Lisans metni", "License text", "Lizenztext", "Texto de la licencia"),
        ("LicenseProgram", "Program lisansı", "Program license", "Programmlizenz", "Licencia del programa"),
        ("LicenseComponents", "Üçüncü taraf bileşenler", "Third-party components", "Komponenten von Drittanbietern", "Componentes de terceros"),
    ]);

    static IReadOnlyDictionary<string, (string, string, string, string)> Build((string Key, string Tr, string En, string De, string Es)[] rows)
    {
        var d = new Dictionary<string, (string, string, string, string)>(StringComparer.Ordinal);
        foreach (var r in rows) d.Add(r.Key, (r.Tr, r.En, r.De, r.Es)); // fails immediately if the same key is written twice
        return d;
    }
}
