#nullable enable

using System;
using System.Diagnostics;
using ManagedWinapi.Windows;

namespace ArtemisEngineeringMidiController.Trainer;

public class ProcessHandle: IDisposable {

    public Process process { get; }
    public IntPtr handle { get; }
    public SystemWindow mainWindow { get; }
    public Version? currentVersion { get; set; }

    internal ProcessHandle(Process process, IntPtr handle) {
        this.process = process;
        this.handle  = handle;
        mainWindow   = new SystemWindow(process.MainWindowHandle);
    }

    private void dispose(bool disposing) {
        _ = Win32.CloseHandle(handle);
        if (disposing) {
            process.Dispose();
        }
    }

    public void Dispose() {
        dispose(true);
        GC.SuppressFinalize(this);
    }

    ~ProcessHandle() {
        dispose(false);
    }

}