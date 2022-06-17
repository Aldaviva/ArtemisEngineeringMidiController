#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ArtemisEngineeringMidiController.Trainer;

namespace ArtemisEngineeringMidiController;

internal static class ProcessHandleProvider {

    public static ProcessHandle? processHandle { get; set; }

}

internal static class BaseAddressProvider {

    public static int? baseAddress {
        get {
            Version?    currentVersion = ProcessHandleProvider.processHandle?.currentVersion;
            GameVersion gameVersion    = ArtemisGame.knownVersions.FirstOrDefault(v => v.version == currentVersion);
            return gameVersion != default ? gameVersion.baseAddress : null;
        }
    }

}

public class Ship {

    private readonly EngineeringSystem beams       = new(0, "Beams", 0x00, 0x00);
    private readonly EngineeringSystem torpedos    = new(1, "Torpedos", 0x20, 0x08);
    private readonly EngineeringSystem sensors     = new(2, "Sensors", 0x40, 0x10);
    private readonly EngineeringSystem maneuvering = new(3, "Maneuvering", 0x60, 0x18);
    private readonly EngineeringSystem impulse     = new(4, "Impulse", 0x80, 0x20);
    private readonly EngineeringSystem warp        = new(5, "Warp", 0xA0, 0x28);
    private readonly EngineeringSystem frontShield = new(6, "Front Shield", 0xC0, 0x30);
    private readonly EngineeringSystem rearShield  = new(7, "Rear Shield", 0xE0, 0x38);

    public IEnumerable<EngineeringSystem> systems { get; }

    public Ship() {
        systems = new List<EngineeringSystem> { beams, torpedos, sensors, maneuvering, impulse, warp, frontShield, rearShield };
    }

    public EngineeringSystem this[EngineeringSystemName name] => name switch {
        EngineeringSystemName.BEAMS        => beams,
        EngineeringSystemName.TORPEDOS     => torpedos,
        EngineeringSystemName.SENSORS      => sensors,
        EngineeringSystemName.MANEUVERING  => maneuvering,
        EngineeringSystemName.IMPULSE      => impulse,
        EngineeringSystemName.WARP         => warp,
        EngineeringSystemName.FRONT_SHIELD => frontShield,
        EngineeringSystemName.REAR_SHIELD  => rearShield,
        _                                  => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

}

public class EngineeringSystem {

    public string name { get; }
    public int index { get; }

    public SystemLevel<float> power { get; }
    public SystemLevel<byte> coolant { get; }
    public SystemLevel<float> heat { get; }
    public SystemLevel<int> maxHealth { get; }
    public SystemLevel<int> damage { get; }

    public EngineeringSystem(int index, string name, int finalPowerCoolantHeatPointerOffset, int finalMaxHealthDamagePointerOffset) {
        this.index = index;
        this.name  = name;
        power      = new SystemLevel<float>("Power", 0, 1, 1 / 3f, new[] { 0x4, 0x4, 0xA4C + finalPowerCoolantHeatPointerOffset });
        coolant    = new SystemLevel<byte>("Coolant", 0, 8, 0, new[] { 0x4, 0x4, 0xA50 + finalPowerCoolantHeatPointerOffset });
        heat       = new SystemLevel<float>("Heat", 0, 1, 0, new[] { 0x4, 0x4, 0xA54 + finalPowerCoolantHeatPointerOffset });
        maxHealth  = new SystemLevel<int>("Maximum Health", 0, 8, 8, new[] { 0x4, 0x4, 0xC58, 0x2F9C + finalMaxHealthDamagePointerOffset });
        damage     = new SystemLevel<int>("Damage", 0, 8, 0, new[] { 0x4, 0x4, 0xC58, 0x2F98 + finalMaxHealthDamagePointerOffset });
    }

}

public enum EngineeringSystemName {

    BEAMS,
    TORPEDOS,
    SENSORS,
    MANEUVERING,
    IMPULSE,
    WARP,
    FRONT_SHIELD,
    REAR_SHIELD

}

public class SystemLevel<T> where T: struct {

    public string name { get; }
    public T minimumValue { get; }
    public T maximumValue { get; }
    public T initialValue { get; }

    public InterprocessMemoryProperty<T> current { get; }

    public SystemLevel(string name, T minimumValue, T maximumValue, T initialValue, IEnumerable<int> memoryOffsetsFromBaseAddress) {
        this.name         = name;
        this.minimumValue = minimumValue;
        this.maximumValue = maximumValue;
        this.initialValue = initialValue;

        current = new InterprocessMemoryPropertyImpl<T>(new VariableBaseIndirectMemoryAddress(() => ProcessHandleProvider.processHandle, null, () => BaseAddressProvider.baseAddress,
            memoryOffsetsFromBaseAddress));
    }

}

internal class VariableBaseIndirectMemoryAddress: IndirectMemoryAddress {

    private readonly Func<ProcessHandle?> processHandleProvider;
    private readonly Func<int?>           getBaseAddress;

    protected override IEnumerable<int> pointerOffsets {
        get {
            int? baseAddress = getBaseAddress();
            if (baseAddress.HasValue) {
                yield return baseAddress.Value;
            } else {
                throw new ArgumentException("Base address was null, the program probably hasn't started yet");
            }

            foreach (int pointerOffset in base.pointerOffsets) {
                yield return pointerOffset;
            }
        }
    }

    protected override ProcessHandle? processHandle => processHandleProvider();

    public VariableBaseIndirectMemoryAddress(Func<ProcessHandle?> processHandleProvider, string? moduleName, Func<int?> getBaseAddress, IEnumerable<int> pointerOffsets): base(processHandleProvider(),
        moduleName, pointerOffsets) {
        this.processHandleProvider = processHandleProvider;
        this.getBaseAddress        = getBaseAddress;
    }

}