using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Aldren.Core.Supervision;

public sealed class ProcessLauncher(ILogger<ProcessLauncher> log) : IProcessLauncher
{
    public IChildProcess Start(string path, string arguments, bool breakAwayFromJob)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The program to start doesn't exist.", path);
        }

        if (breakAwayFromJob)
        {
            try
            {
                return Create(path, arguments, PROCESS_CREATION_FLAGS.CREATE_BREAKAWAY_FROM_JOB);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_ACCESS_DENIED)
            {
                // Core runs in a job that forbids breakaway (some terminals and IDEs do this): start it normally.
                log.LogInformation("Breakaway from the job was refused; starting {Path} normally", path);
            }
        }

        return Create(path, arguments, 0);
    }

    public IChildProcess? Attach(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return process.HasExited ? null : new ChildProcess(process);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static unsafe IChildProcess Create(string path, string arguments, PROCESS_CREATION_FLAGS flags)
    {
        var commandLine = $"\"{path}\" {arguments}".TrimEnd() + "\0";
        var buffer = commandLine.ToCharArray();
        var startup = new STARTUPINFOW { cb = (uint)sizeof(STARTUPINFOW) };
        PROCESS_INFORMATION info;
        fixed (char* application = path)
        fixed (char* command = buffer)
        fixed (char* directory = Path.GetDirectoryName(path))
        {
            if (!PInvoke.CreateProcess(
                    application, command, null, null, false, flags, null, directory, &startup, &info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        PInvoke.CloseHandle(info.hThread);
        try
        {
            return new ChildProcess(Process.GetProcessById((int)info.dwProcessId));
        }
        finally
        {
            PInvoke.CloseHandle(info.hProcess);
        }
    }

    private sealed class ChildProcess(Process process) : IChildProcess
    {
        public int Id { get; } = process.Id;

        public bool HasExited => process.HasExited;

        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);

        public void Dispose() => process.Dispose();
    }
}
