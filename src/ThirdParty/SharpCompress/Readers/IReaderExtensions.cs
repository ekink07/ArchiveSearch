using System;
using System.IO;
using SharpCompress.Common;
using SharpCompress.IO;

namespace SharpCompress.Readers;

public static class IReaderExtensions
{
    public static void WriteEntryTo(this IReader reader, string filePath)
    {
        using Stream stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
        reader.WriteEntryTo(stream);
    }

    public static void WriteEntryTo(this IReader reader, FileInfo filePath)
    {
        using Stream stream = filePath.Open(FileMode.Create);
        reader.WriteEntryTo(stream);
    }

    /// <summary>
    /// Extract all remaining unread entries to specific directory, retaining filename
    /// </summary>
    public static void WriteAllToDirectory(this IReader reader,
        string destinationDirectory,
        ExtractionOptions? options = null
    )
    {
        while (reader.MoveToNextEntry())
        {
            reader.WriteEntryToDirectory(destinationDirectory, options);
        }
    }

    /// <summary>
    /// Extract to specific directory, retaining filename
    /// </summary>
    public static void WriteEntryToDirectory(this IReader reader,
        string destinationDirectory,
        ExtractionOptions? options = null
    ) =>
        reader.Entry.WriteEntryToDirectory(
            destinationDirectory,
            options,
            (path) => reader.WriteEntryToFile(path, options)
        );

    /// <summary>
    /// Extract to specific file
    /// </summary>
    public static void WriteEntryToFile(this IReader reader, string destinationFileName, ExtractionOptions? options = null)
    {
        options ??= new ExtractionOptions();
        reader.Entry.WriteEntryToFile(
            destinationFileName,
            options,
            (x, fm) =>
            {
                using var fs = File.Open(x, fm);
                CopyEntryTo(reader, fs, options ?? new ExtractionOptions());
            }
        );
    }

    private static void CopyEntryTo(
        IReader reader,
        Stream writableStream,
        ExtractionOptions options
    )
    {
        using var entryStream = reader.OpenEntryStream();
        var checkedStream = IEntryExtensions.WrapWithChecksumValidation(
            reader.Entry,
            entryStream,
            options
        );
        var sourceStream = WrapWithProgress(checkedStream, reader.Entry);
        sourceStream.CopyTo(writableStream, options.BufferSize);
    }

    private static Stream WrapWithProgress(Stream source, IEntry entry)
    {
        var progress = entry.Options.Progress;
        if (progress is null)
        {
            return source;
        }

        var entryPath = entry.Key ?? string.Empty;
        var totalBytes = GetEntrySizeSafe(entry);
        return new ProgressReportingStream(
            source,
            progress,
            entryPath,
            totalBytes,
            leaveOpen: true
        );
    }

    private static long? GetEntrySizeSafe(IEntry entry)
    {
        try
        {
            var size = entry.Size;
            return size >= 0 ? size : null;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }
}
