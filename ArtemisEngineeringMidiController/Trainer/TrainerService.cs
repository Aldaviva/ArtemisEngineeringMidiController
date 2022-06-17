#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KoKo.Property;

namespace ArtemisEngineeringMidiController.Trainer;

internal interface TrainerService: IDisposable {

    Property<AttachmentState> isAttachedToGame { get; }

    void attachToGame(Game game);

    void monitorProperty(ManuallyRecalculatedProperty property);

    void stopMonitoringProperty(ManuallyRecalculatedProperty property);

}

internal class TrainerServiceImpl: TrainerService {

    private readonly StoredProperty<AttachmentState> attachmentState = new();
    public Property<AttachmentState> isAttachedToGame { get; }

    private readonly CancellationTokenSource cancellationTokenSource = new();
    private          Task?                   monitorTask;

    private readonly ISet<ManuallyRecalculatedProperty> monitoredProperties = new HashSet<ManuallyRecalculatedProperty>();

    public TrainerServiceImpl() {
        isAttachedToGame = attachmentState;

        attachmentState.PropertyChanged += (_, args) => Trace.WriteLine($"Trainer state: {args.NewValue}");
    }

    public void monitorProperty(ManuallyRecalculatedProperty property) {
        monitoredProperties.Add(property);
    }

    public void stopMonitoringProperty(ManuallyRecalculatedProperty property) {
        monitoredProperties.Remove(property);
    }

    public void attachToGame(Game game) {
        if (monitorTask is not null) {
            throw new ApplicationException("Cannot attach the same TrainerServiceImpl instance to a game more than once.");
        }

        monitorTask = Task.Run(async () => {
            Process? gameProcess = null;
            // ProcessHandle? gameProcessHandle = null;

            while (!cancellationTokenSource.IsCancellationRequested) {
                await Task.Delay(attachmentState.Value switch {
                    AttachmentState.TRAINER_STOPPED                  => TimeSpan.Zero,
                    AttachmentState.ATTACHED                         => TimeSpan.FromMilliseconds(200),
                    AttachmentState.MEMORY_ADDRESS_NOT_FOUND         => TimeSpan.FromSeconds(2),
                    AttachmentState.MEMORY_ADDRESS_COULD_NOT_BE_READ => TimeSpan.FromSeconds(2),
                    AttachmentState.PROGRAM_NOT_RUNNING              => TimeSpan.FromSeconds(10),
                    _                                                => throw new ArgumentOutOfRangeException()
                }, cancellationTokenSource.Token);

                gameProcess ??= findProcess(game);
                if (gameProcess?.HasExited ?? true) {
                    attachmentState.Value = AttachmentState.PROGRAM_NOT_RUNNING;
                    gameProcess?.Dispose();
                    gameProcess = null;
                    ProcessHandleProvider.processHandle?.Dispose();
                    ProcessHandleProvider.processHandle = null;
                    continue;
                }

                ProcessHandleProvider.processHandle ??= MemoryEditor.openProcess(gameProcess);
                if (ProcessHandleProvider.processHandle == null) {
                    attachmentState.Value = AttachmentState.PROGRAM_NOT_RUNNING;
                    continue;
                }

                ProcessHandleProvider.processHandle.currentVersion ??= getGameVersion(game, ProcessHandleProvider.processHandle);

                try {
                    foreach (ManuallyRecalculatedProperty property in monitoredProperties) {
                        property.Recalculate();
                    }

                    attachmentState.Value = AttachmentState.ATTACHED;
                } catch (ApplicationException e) {
                    Trace.WriteLine("ApplicationException: " + e);
                    attachmentState.Value = AttachmentState.MEMORY_ADDRESS_NOT_FOUND;
                } catch (Win32Exception e) {
                    Trace.WriteLine($"Win32Exception: (NativeErrorCode = {e.NativeErrorCode}) " + e);
                    attachmentState.Value = AttachmentState.MEMORY_ADDRESS_COULD_NOT_BE_READ;
                    if (e.NativeErrorCode != 299) {
                        Console.WriteLine(e);
                    }

                    Trace.WriteLine("Memory address could not be read");
                    Trace.WriteLine(e);
                }
            }

            gameProcess?.Dispose();
            ProcessHandleProvider.processHandle?.Dispose();

        }, cancellationTokenSource.Token);
    }

    protected virtual Process? findProcess(Game game) {
        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.processName)).FirstOrDefault();
    }

    protected virtual Version? getGameVersion(Game game, ProcessHandle processHandle) {
        return null;
    }

    public void Dispose() {
        cancellationTokenSource.Cancel();
        try {
            monitorTask?.GetAwaiter().GetResult();
        } catch (TaskCanceledException) {
            //cancellation is how this task normally ends
        }

        attachmentState.Value = AttachmentState.TRAINER_STOPPED;
    }

}

internal enum AttachmentState {

    TRAINER_STOPPED,
    PROGRAM_NOT_RUNNING,
    MEMORY_ADDRESS_NOT_FOUND,
    MEMORY_ADDRESS_COULD_NOT_BE_READ,
    ATTACHED

}