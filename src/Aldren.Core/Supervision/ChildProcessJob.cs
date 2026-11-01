using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Aldren.Core.Supervision;

/// <summary>
/// A Job Object that kills its processes when Core goes away. llama-server and the gateway join it in later phases;
/// the App never does, so it can relaunch Core.
/// </summary>
public sealed unsafe class ChildProcessJob : IDisposable
{
    private readonly SafeFileHandle _job;

    public ChildProcessJob()
    {
        _job = PInvoke.CreateJobObject(null, (string?)null);
        if (_job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!PInvoke.SetInformationJobObject(
                (Windows.Win32.Foundation.HANDLE)_job.DangerousGetHandle(), JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Add(IChildProcess child)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_SET_QUOTA | PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, (uint)child.Id);
        if (process.IsInvalid || !PInvoke.AssignProcessToJobObject(_job, process))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose() => _job.Dispose();
}
