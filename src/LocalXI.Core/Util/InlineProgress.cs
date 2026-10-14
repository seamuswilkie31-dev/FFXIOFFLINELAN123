namespace LocalXI.Core.Util;

/// <summary>
/// Reports progress synchronously on the calling thread. Unlike <see cref="Progress{T}"/> it doesn't post to a
/// captured context, so messages forwarded to another IProgress stay in order.
/// </summary>
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
