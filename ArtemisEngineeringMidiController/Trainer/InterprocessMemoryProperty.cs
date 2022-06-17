#nullable enable

using KoKo.Property;

namespace ArtemisEngineeringMidiController.Trainer;

// public interface InterprocessMemoryProperty: ManuallyRecalculatedProperty {
//
//     // public ProcessHandle? processHandle { get; set; }
//
// }

public interface InterprocessMemoryProperty<T>: ManuallyRecalculatedProperty, /*InterprocessMemoryProperty,*/ Property<T?> where T: struct {

    // ReSharper disable once InconsistentNaming - match library naming
    new T? Value { get; set; }

}

public class InterprocessMemoryPropertyImpl<T>: ManuallyRecalculatedProperty<T?>, InterprocessMemoryProperty<T> where T: struct {

    private readonly MemoryAddress memoryAddress;

    protected ProcessHandle? processHandle => ProcessHandleProvider.processHandle;

    public InterprocessMemoryPropertyImpl(MemoryAddress memoryAddress) {
        this.memoryAddress = memoryAddress;
    }

    protected override T? ComputeValue() {
        if (processHandle == null) return null;

        return MemoryEditor.readFromProcessMemory<T>(processHandle, memoryAddress);
    }

    private void writeValueToOtherProcess(T? newValue) {
        ProcessHandle? procHandle = processHandle;
        if (!newValue.HasValue || procHandle == null) return;

        MemoryEditor.writeToProcessMemory(procHandle, memoryAddress, newValue.Value);
        Recalculate();
    }

    public new T? Value {
        get => base.Value;
        set => writeValueToOtherProcess(value);
    }

}