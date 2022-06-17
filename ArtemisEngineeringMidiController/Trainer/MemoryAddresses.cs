#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ArtemisEngineeringMidiController.Trainer;

public interface MemoryAddress {

    public IntPtr address { get; }

}

public readonly struct FixedMemoryAddress: MemoryAddress {

    public IntPtr address { get; }

    public FixedMemoryAddress(IntPtr address) {
        this.address = address;
    }

}

public class IndirectMemoryAddress: MemoryAddress {

    private readonly string? moduleName;

    protected virtual ProcessHandle? processHandle { get; }
    protected virtual IEnumerable<int> pointerOffsets { get; }

    /// <param name="processHandle"></param>
    /// <param name="moduleName">The name of the module to which the offsets are relative, such as <c>UnityPlayer.dll</c>, or <c>null</c> to use the process' main module.</param>
    /// <param name="pointerOffsets"></param>
    public IndirectMemoryAddress(ProcessHandle? processHandle, string? moduleName, IEnumerable<int> pointerOffsets) {
        this.processHandle  = processHandle;
        this.moduleName     = moduleName;
        this.pointerOffsets = pointerOffsets;
    }

    public IntPtr address {
        get {
            ProcessHandle procHandle = processHandle ?? throw new ArgumentException("Process handle is null");

            /*
             * Is the game 32-bit or 64-bit?
             * Note that the trainer must be compiled as 64-bit in order to read memory from 64-bit games.
             */
            int targetProcessWordLengthBytes = Win32.isProcess64Bit(procHandle.process) ? Marshal.SizeOf<long>() : Marshal.SizeOf<int>();

            IntPtr memoryAddress = MemoryEditor.getModuleBaseAddressByName(procHandle, moduleName) ??
                throw new ArgumentException($"No module with name {moduleName} found in process {procHandle.process.ProcessName}");

            bool isFirstOffset = true;
            foreach (int offset in pointerOffsets) {
                if (isFirstOffset) {
                    isFirstOffset = false;
                    memoryAddress = IntPtr.Add(memoryAddress, offset);
                } else {
                    bool success = Win32.ReadProcessMemory(procHandle.handle, memoryAddress, out IntPtr memoryValue, targetProcessWordLengthBytes, out long _);
                    if (!success) {
                        throw new ApplicationException($"Could not read memory address 0x{memoryAddress.ToInt64():X}: {Marshal.GetLastWin32Error()}");
                    }

                    memoryAddress = IntPtr.Add(memoryValue, offset);
                }
            }

            return memoryAddress;
        }
    }

}