using System.ComponentModel;

namespace ArsivAra.Core;

/// <summary>
/// Bir NTFS biriminin USN Journal'ını izler (FileSystemWatcher'ın aksine tampon taşması yok,
/// uygulama kapalıyken bile kayıt tutulur). Arşiv dosyalarındaki ve klasörlerin taşınma/silinmesindeki
/// değişiklikleri tam yol olarak bildirir.
/// </summary>
public sealed class UsnWatcher : IDisposable
{
    const uint USN_REASON_DATA_OVERWRITE = 0x1;
    const uint USN_REASON_DATA_EXTEND = 0x2;
    const uint USN_REASON_DATA_TRUNCATION = 0x4;
    const uint USN_REASON_FILE_CREATE = 0x100;
    const uint USN_REASON_FILE_DELETE = 0x200;
    const uint USN_REASON_RENAME_OLD_NAME = 0x1000;
    const uint USN_REASON_RENAME_NEW_NAME = 0x2000;
    const uint USN_REASON_BASIC_INFO_CHANGE = 0x8000;
    const uint USN_REASON_HARD_LINK_CHANGE = 0x10000;
    const uint USN_REASON_CLOSE = 0x80000000;

    const uint FileInteresting = USN_REASON_DATA_OVERWRITE | USN_REASON_DATA_EXTEND | USN_REASON_DATA_TRUNCATION |
        USN_REASON_FILE_CREATE | USN_REASON_FILE_DELETE | USN_REASON_RENAME_OLD_NAME | USN_REASON_RENAME_NEW_NAME |
        USN_REASON_BASIC_INFO_CHANGE | USN_REASON_HARD_LINK_CHANGE | USN_REASON_CLOSE;
    const uint DirInteresting = USN_REASON_FILE_DELETE | USN_REASON_RENAME_OLD_NAME | USN_REASON_RENAME_NEW_NAME;

    readonly NtfsVolume _volume;
    readonly VolumeJournal _journal;
    readonly CancellationTokenSource _cts = new();
    readonly Thread _thread;

    /// <summary>Değişen arşivin ya da taşınan/silinen klasörün tam yolu.</summary>
    public event Action<string>? PathChanged;

    /// <summary>Üst klasörü artık bulunamayan (birlikte silinmiş) öğenin adı.</summary>
    public event Action<string>? NameRemoved;

    /// <summary>Journal silindi/taştı: tam yeniden tarama gerekir.</summary>
    public event Action<string>? ResetRequired;

    public long CurrentUsn { get; private set; }

    public UsnWatcher(NtfsVolume volume, VolumeJournal journal)
    {
        _volume = volume;
        _journal = journal;
        CurrentUsn = journal.StartUsn;
        _thread = new Thread(Run) { IsBackground = true, Name = "USN " + volume.Root, Priority = ThreadPriority.BelowNormal };
    }

    public static UsnWatcher? TryStart(VolumeJournal journal)
    {
        var vol = NtfsVolume.TryOpen(journal.Root);
        if (vol == null) return null;
        var w = new UsnWatcher(vol, journal);
        return w;
    }

    public void Start() => _thread.Start();

    void Run()
    {
        var buffer = new byte[1 << 16];
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CurrentUsn = _volume.ReadJournal(_journal.JournalId, CurrentUsn, Handle, buffer);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is NtfsVolume.ERROR_JOURNAL_ENTRY_DELETED
                                               or NtfsVolume.ERROR_JOURNAL_NOT_ACTIVE
                                               or NtfsVolume.ERROR_JOURNAL_DELETE_IN_PROGRESS
                                               or 87 /* ERROR_INVALID_PARAMETER: journal kimliği değişti */)
            {
                ResetRequired?.Invoke($"{_volume.Root} USN Journal sıfırlandı ({ex.Message})");
                return;
            }
            catch (Exception ex)
            {
                ResetRequired?.Invoke($"{_volume.Root} USN Journal okunamadı ({ex.Message})");
                return;
            }
            ct.WaitHandle.WaitOne(1000);
        }
    }

    void Handle(UsnEntry e)
    {
        if (e.IsDirectory)
        {
            if ((e.Reason & DirInteresting) == 0) return;
        }
        else
        {
            if ((e.Reason & FileInteresting) == 0 || !ArchiveReader.IsArchiveName(e.Name)) return;
        }

        // Kayıttaki ad, olay anındaki ad; üst klasörün güncel yolunu kimliğinden çözüyoruz.
        string? dir = _volume.PathFromId(e.ParentRef);
        if (dir == null)
        {
            NameRemoved?.Invoke(e.Name);
            return;
        }
        PathChanged?.Invoke(Path.Combine(dir, e.Name));
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(2000);
        _volume.Dispose();
        _cts.Dispose();
    }
}


