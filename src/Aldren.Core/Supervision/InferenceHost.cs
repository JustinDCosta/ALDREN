using System.Diagnostics;
using Aldren.Common;
using Aldren.Inference;

namespace Aldren.Core.Supervision;

/// <summary>llama-server and the cloud gateway go in a kill-on-close Job Object, so they never outlive Core (ARCHITECTURE §4).</summary>
public sealed class InferenceHost(IProcessLauncher launcher, AldrenPaths paths) : IInferenceHost, Aldren.Cloud.IProcessJob, IDisposable
{
    private readonly ChildProcessJob _job = new();

    public string LogDirectory => Path.Combine(paths.LogsDirectory, "llama");

    public void AddToJob(Process process)
    {
        using var child = launcher.Attach(process.Id);
        if (child is not null)
        {
            _job.Add(child);
        }
    }

    void Aldren.Cloud.IProcessJob.Add(Process process) => AddToJob(process);

    public void Dispose() => _job.Dispose();
}
