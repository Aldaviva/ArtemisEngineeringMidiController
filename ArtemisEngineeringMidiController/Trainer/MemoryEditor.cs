#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ArtemisEngineeringMidiController.Trainer;

internal static class MemoryEditor {

    public static ProcessHandle? openProcess(Process targetProcess) {
        IntPtr handle = Win32.OpenProcess(
            Win32.ProcessAccessFlags.VIRTUAL_MEMORY_READ |
            Win32.ProcessAccessFlags.VIRTUAL_MEMORY_WRITE |
            Win32.ProcessAccessFlags.VIRTUAL_MEMORY_OPERATION,
            false, targetProcess.Id);
        return handle != IntPtr.Zero ? new ProcessHandle(targetProcess, handle) : null;
    }

    public static T readFromProcessMemory<T>(ProcessHandle processHandle, MemoryAddress memoryAddress, int? bytesToRead = null) {
        bytesToRead ??= Marshal.SizeOf<T>();

        byte[] buffer = new byte[(int) bytesToRead];

        bool readSuccess = Win32.ReadProcessMemory(processHandle.handle, memoryAddress.address, buffer, buffer.Length, out long bytesRead);

        if (!readSuccess || bytesRead == 0 || bytesRead > buffer.Length) {
            throw new ApplicationException($"read wrong number of bytes (expected {buffer.Length:N0}, actual {bytesRead:N0})");
        }

        return convertBufferToType<T>(buffer);
    }

    public static void writeToProcessMemory<T>(ProcessHandle processHandle, MemoryAddress memoryAddress, T value) {
        byte[] buffer = convertValueToBuffer(value);

        bool writeSuccess = Win32.WriteProcessMemory(processHandle.handle, memoryAddress.address, buffer, buffer.Length, out IntPtr bytesWritten);

        if (!writeSuccess || bytesWritten.ToInt32() != buffer.Length) {
            throw new ApplicationException($"wrote wrong number of bytes (expected {buffer.Length:N0}, actual {bytesWritten.ToInt32():N0})");
        }
    }

    public static IntPtr? getModuleBaseAddressByName(ProcessHandle processHandle, string? moduleName) {
        return moduleName == null ? processHandle.process.MainModule?.BaseAddress : Win32.getModuleBaseAddress(new IntPtr(processHandle.process.Id), moduleName);
    }

    private static byte[] convertValueToBuffer<T>(T value) {
        object valueObject = value!;

        return Type.GetTypeCode(typeof(T)) switch {
            TypeCode.String => Encoding.Unicode.GetBytes((string) valueObject),
            TypeCode.Int32  => BitConverter.GetBytes((int) valueObject),
            TypeCode.Byte   => new[] { (byte) valueObject },
            TypeCode.Single => BitConverter.GetBytes((float) valueObject),
            _               => throw new ArgumentOutOfRangeException("T", Type.GetTypeCode(typeof(T)), "unhandled type code")
        };
    }

    private static T convertBufferToType<T>(byte[] buffer) {
        return (T) (object) (Type.GetTypeCode(typeof(T)) switch {
            TypeCode.String => Encoding.Unicode.GetString(buffer, 0, buffer.Length).Split(new[] { (char) 0 }, 2)[0],
            TypeCode.Int32  => BitConverter.ToInt32(convertBufferToMachineEndianness(buffer), 0),
            TypeCode.Byte   => buffer[0],
            TypeCode.Single => BitConverter.ToSingle(convertBufferToMachineEndianness(buffer), 0),
            _               => throw new ArgumentOutOfRangeException("T", Type.GetTypeCode(typeof(T)), "unhandled type code")
        });
    }

    private static byte[] convertBufferToMachineEndianness(byte[] buffer) {
        return BitConverter.IsLittleEndian ? buffer : buffer.Reverse().ToArray();
    }

}