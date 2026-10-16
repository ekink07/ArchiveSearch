using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;

namespace SharpCompress;

public static class AsyncEnumerableEx
{
    public static async IAsyncEnumerable<T> Empty<T>()
        where T : notnull
    {
        await Task.Yield();
        yield break;
    }
}

public static class EnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
    }
}

public static class AsyncEnumerableExtensions
{
#if !NET10_0_OR_GREATER
    public static async IAsyncEnumerable<TResult> Select<T, TResult>(this IAsyncEnumerable<T> source, Func<T, TResult> selector)
    {
        await foreach (var element in source.ConfigureAwait(false))
        {
            yield return selector(element);
        }
    }

    public static async ValueTask<int> CountAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        await using var e = source.GetAsyncEnumerator(cancellationToken);

        var count = 0;
        while (await e.MoveNextAsync().ConfigureAwait(false))
        {
            checked
            {
                count++;
            }
        }

        return count;
    }

    public static async IAsyncEnumerable<T> Take<T>(this IAsyncEnumerable<T> source, int count)
    {
        await foreach (var element in source.ConfigureAwait(false))
        {
            yield return element;

            if (--count == 0)
            {
                break;
            }
        }
    }

    public static async ValueTask<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source.ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }

    public static async ValueTask<bool> AllAsync<T>(this IAsyncEnumerable<T> source, Func<T, bool> predicate)
    {
        await foreach (var item in source.ConfigureAwait(false))
        {
            if (!predicate(item))
            {
                return false;
            }
        }

        return true;
    }

    public static async IAsyncEnumerable<T> Where<T>(this IAsyncEnumerable<T> source, Func<T, bool> predicate)
    {
        await foreach (var item in source.ConfigureAwait(false))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    public static async ValueTask<T> SingleAsync<T>(this IAsyncEnumerable<T> source, Func<T, bool>? predicate = null)
    {
        IAsyncEnumerator<T> enumerator;
        if (predicate is null)
        {
            enumerator = source.GetAsyncEnumerator();
        }
        else
        {
            enumerator = source.Where(predicate).GetAsyncEnumerator();
        }

        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            throw new ArchiveOperationException("The source sequence is empty.");
        }
        var value = enumerator.Current;
        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            throw new ArchiveOperationException(
                "The source sequence contains more than one element."
            );
        }
        return value;
    }

    public static async ValueTask<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source.ConfigureAwait(false))
        {
            return item;
        }
        throw new ArchiveOperationException("The source sequence is empty.");
    }

    public static async ValueTask<T?> FirstOrDefaultAsync<T>(this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            return item;
        }

        return default;
    }
#endif

    public static async IAsyncEnumerable<TResult> CastAsync<TResult>(
        this IAsyncEnumerable<object?> source
    )
        where TResult : class
    {
        await foreach (var item in source.ConfigureAwait(false))
        {
            yield return (item as TResult).NotNull();
        }
    }

    public static async ValueTask<TAccumulate> AggregateAsync<TAccumulate, T>(
        this IAsyncEnumerable<T> source,
        TAccumulate seed,
        Func<TAccumulate, T, TAccumulate> func
    )
    {
        var result = seed;
        await foreach (var element in source.ConfigureAwait(false))
        {
            result = func(result, element);
        }
        return result;
    }
}
