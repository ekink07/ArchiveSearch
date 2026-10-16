using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.IO;

namespace SharpCompress;

public static class StreamExtensions
{
    public static void Skip(this Stream stream, long advanceAmount)
    {
        if (stream.CanSeek && stream is not SharpCompressStream)
        {
            stream.Position += advanceAmount;
            return;
        }

        using var readOnlySubStream = new ReadOnlySubStream(stream, advanceAmount);
        readOnlySubStream.CopyTo(Stream.Null);
    }

    public static void Skip(this Stream stream) => stream.CopyTo(Stream.Null);

    public static async ValueTask SkipAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if NET6_0_OR_GREATER
        await stream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
#else
        await stream.CopyToAsync(Stream.Null).ConfigureAwait(false);
#endif
    }

    internal static int Read(this Stream stream, Span<byte> buffer)
    {
        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            var read = stream.Read(temp, 0, buffer.Length);

            temp.AsSpan(0, read).CopyTo(buffer);

            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }

    internal static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);

        buffer.CopyTo(temp);

        try
        {
            stream.Write(temp, 0, buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }
}
