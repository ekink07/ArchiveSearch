using System.ComponentModel;

namespace ArsivAra.Core;

/// <summary>
/// Watches an NTFS volume's USN Journal (unlike FileSystemWatcher there is no buffer overflow,
/// and records are kept even while the app is closed). Reports changes to archive files and
/// folder moves/deletions as full paths.
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

    /// <summary>Full path of an archive file that was added, changed, deleted or renamed.</summary>
    public event Action<string>? PathChanged;

    /// <summary>Full path of a folder that was moved, renamed or deleted (the archives under it must be rechecked).</summary>
    public event Action<string>? DirectoryChanged;

    /// <summary>Name of an item whose parent folder can no longer be found (deleted along with it).</summary>
    public event Action<string>? NameRemoved;

    /// <summary>The journal was deleted or overflowed: a full rescan is required.</summary>
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
                                               or 87 /* ERROR_INVALID_PARAMETER: the journal ID changed */)
            {
                ResetRequired?.Invoke($"{_volume.Root} USN Journal was reset ({ex.Message})");
                return;
            }
            catch (Exception ex)
            {
                ResetRequired?.Invoke($"{_volume.Root} USN Journal could not be read ({ex.Message})");
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

        // The name in the record is the name at the time of the event; we resolve the parent folder's current path from its ID.
        string? dir = _volume.PathFromId(e.ParentRef);
        if (dir == null)
        {
            NameRemoved?.Invoke(e.Name);
            return;
        }
        string path = Path.Combine(dir, e.Name);
        if (e.IsDirectory) DirectoryChanged?.Invoke(path);
        else PathChanged?.Invoke(path);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(2000);
        _volume.Dispose();
        _cts.Dispose();
    }
}


