namespace Aldren.Common;

/// <summary>
/// One process per name, decided by whether the named mutex already existed rather than by waiting on it:
/// a mutex is re-entrant for the thread that owns it.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string CoreName = @"Local\Aldren.Core";
    public const string AppName = @"Local\Aldren.App";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex);
    }

    public static bool IsHeld(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    public void Dispose() => _mutex.Dispose();
}
