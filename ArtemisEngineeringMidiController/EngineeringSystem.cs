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

    public const int SYSTEM_COUNT = 8;
    public const int MAX_COOLANT  = 8;

    private readonly EngineeringSystem beams       = new(0, EngineeringSystemName.BEAMS, 0x00, 0x00);
    private readonly EngineeringSystem torpedos    = new(1, EngineeringSystemName.TORPEDOS, 0x20, 0x08);
    private readonly EngineeringSystem sensors     = new(2, EngineeringSystemName.SENSORS, 0x40, 0x10);
    private readonly EngineeringSystem maneuvering = new(3, EngineeringSystemName.MANEUVERING, 0x60, 0x18);
    private readonly EngineeringSystem impulse     = new(4, EngineeringSystemName.IMPULSE, 0x80, 0x20);
    private readonly EngineeringSystem warp        = new(5, EngineeringSystemName.WARP, 0xA0, 0x28);
    private readonly EngineeringSystem frontShield = new(6, EngineeringSystemName.FRONT_SHIELD, 0xC0, 0x30);
    private readonly EngineeringSystem rearShield  = new(7, EngineeringSystemName.REAR_SHIELD, 0xE0, 0x38);

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

    private static readonly GameClicker<float> POWER_CLICKER   = new PowerClicker();
    private static readonly GameClicker<byte>  COOLANT_CLICKER = new CoolantClicker();

    public EngineeringSystemName name { get; }

    /// <summary>
    /// 0-indexed
    /// </summary>
    public int column { get; }

    public WritableSystemLevel<float> power { get; }
    public WritableSystemLevel<byte> coolant { get; }
    public SystemLevel<float> heat { get; }
    public SystemLevel<int> maxHealth { get; }
    public SystemLevel<int> damage { get; }

    public EngineeringSystem(int column, EngineeringSystemName name, int finalPowerCoolantHeatPointerOffset, int finalMaxHealthDamagePointerOffset) {
        this.column = column;
        this.name   = name;
        // these memory offsets only apply to ship 1 (Artemis) and seem to only work in co-op, not pvp or single-player
        power     = new WritableSystemLevel<float>(column, name, WritableSystemLevelName.POWER, 0, 1, 1 / 3f, new[] { 0x4, 0x4, 0xA4C + finalPowerCoolantHeatPointerOffset }, POWER_CLICKER);
        coolant = new WritableSystemLevel<byte>(column, name, WritableSystemLevelName.COOLANT, 0, Ship.MAX_COOLANT, 0, new[] { 0x4, 0x4, 0xA50 + finalPowerCoolantHeatPointerOffset },
            COOLANT_CLICKER);
        heat      = new SystemLevel<float>(name, SystemLevelName.HEAT, 0, 1, 0, new[] { 0x4, 0x4, 0xA54 + finalPowerCoolantHeatPointerOffset });
        maxHealth = new SystemLevel<int>(name, SystemLevelName.MAXIMUM_HEALTH, 0, 8, 8, new[] { 0x4, 0x4, 0xC58, 0x2F9C + finalMaxHealthDamagePointerOffset });
        damage    = new SystemLevel<int>(name, SystemLevelName.DAMAGE, 0, 8, 0, new[] { 0x4, 0x4, 0xC58, 0x2F98 + finalMaxHealthDamagePointerOffset });
    }
    //
    // public Point getPowerClickPosition(float forValue) { }
    //
    // public Point getCoolantClickPosition(byte forValue) { }

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

    public EngineeringSystemName systemName { get; }
    public SystemLevelName levelName { get; }
    public T minimum { get; }
    public T maximum { get; }
    public T initial { get; }

    protected readonly MemoryAddress memoryAddress;

    public InterprocessMemoryProperty<T> current { get; }

    public SystemLevel(EngineeringSystemName systemName, SystemLevelName levelName, T minimum, T maximum, T initial, IEnumerable<int> memoryOffsetsFromBaseAddress) {
        this.systemName = systemName;
        this.levelName  = levelName;
        this.minimum    = minimum;
        this.maximum    = maximum;
        this.initial    = initial;

        memoryAddress = new VariableBaseIndirectMemoryAddress(() => ProcessHandleProvider.processHandle, null, () => BaseAddressProvider.baseAddress,
            memoryOffsetsFromBaseAddress);

        current = new InterprocessMemoryProperty<T>(memoryAddress);
    }

}

public class WritableSystemLevel<T>: SystemLevel<T> where T: struct {

    private readonly int uiColumn;

    public new WritableInterprocessMemoryProperty<T> current { get; }

    public WritableSystemLevel(int uiColumn, EngineeringSystemName systemName, WritableSystemLevelName levelName, T minimum, T maximum, T initial, IEnumerable<int> memoryOffsetsFromBaseAddress,
                               GameClicker<T> gameClicker): base(systemName, (SystemLevelName) levelName, minimum, maximum, initial, memoryOffsetsFromBaseAddress) {
        this.uiColumn = uiColumn;

        current = new WritableInterprocessMemoryProperty<T>(memoryAddress, uiColumn, gameClicker);
    }

}

public enum SystemLevelName {

    POWER   = WritableSystemLevelName.POWER,
    COOLANT = WritableSystemLevelName.COOLANT,
    HEAT,
    MAXIMUM_HEALTH,
    DAMAGE

}

public enum WritableSystemLevelName {

    POWER,
    COOLANT

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