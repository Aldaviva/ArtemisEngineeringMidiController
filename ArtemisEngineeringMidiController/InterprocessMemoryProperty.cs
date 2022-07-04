#nullable enable

using ArtemisEngineeringMidiController.Trainer;
using KoKo.Property;

namespace ArtemisEngineeringMidiController;

public class InterprocessMemoryProperty<T>: ManuallyRecalculatedProperty<T?> where T: struct {

    private readonly MemoryAddress memoryAddress;

    protected ProcessHandle? processHandle => ProcessHandleProvider.processHandle;

    public InterprocessMemoryProperty(MemoryAddress memoryAddress) {
        this.memoryAddress = memoryAddress;
    }

    protected override T? ComputeValue() {
        if (processHandle == null) return null;

        return MemoryEditor.readFromProcessMemory<T>(processHandle, memoryAddress);
    }

}

public class WritableInterprocessMemoryProperty<T>: InterprocessMemoryProperty<T>, SettableProperty<T?> where T: struct {

    private readonly int uiColumn;

    // private readonly WritableSystemLevelName level;
    private readonly GameClicker<T>          clicker;

    public new T? Value {
        get => base.Value;
        set => writeValueToOtherProcess(value);
    }

    public WritableInterprocessMemoryProperty(MemoryAddress memoryAddress, int uiColumn, GameClicker<T> clicker): base(memoryAddress) {
        this.uiColumn = uiColumn;
        // this.level   = level;
        this.clicker = clicker;
    }

    private void writeValueToOtherProcess(T? newValue) {
        ProcessHandle? procHandle = processHandle;
        if (newValue.HasValue && procHandle != null) {
            clicker.setSystemLevel(procHandle, uiColumn, Value, newValue.Value);
            Recalculate();
        }
    }

}
