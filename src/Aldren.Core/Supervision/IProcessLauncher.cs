namespace Aldren.Core.Supervision;

public interface IProcessLauncher
{
    /// <param name="breakAwayFromJob">Leave any Job Object Core itself runs in, so the child outlives Core.</param>
    IChildProcess Start(string path, string arguments, bool breakAwayFromJob);

    /// <summary>A process that is already running; null when it has gone.</summary>
    IChildProcess? Attach(int processId);
}

public interface IChildProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken ct);
}
