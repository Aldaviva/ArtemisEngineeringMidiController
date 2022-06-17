#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using ArtemisEngineeringMidiController.Trainer;
using BehringerXTouchExtender;
using KoKo.Property;
using ManagedWinapi.Windows;

namespace ArtemisEngineeringMidiController;

public static class TestMain {

    private static readonly ManualResetEventSlim READY_TO_EXIT = new();

    public static void Main() {
        ArtemisManager artemisManager = new();
        artemisManager.start();
        READY_TO_EXIT.Wait();
    }

}

public class ArtemisManager: IDisposable {

    private readonly TrainerService trainerService = new ArtemisTrainerService();

    private readonly IRelativeBehringerXTouchExtender midiController = BehringerXTouchExtenderFactory.CreateWithRelativeMode();

    public void start() {
        midiController.Open();

        Ship ship = new();

        foreach (EngineeringSystem engineeringSystem in ship.systems) {
            int trackId = engineeringSystem.index;

            trainerService.monitorProperty(engineeringSystem.power.current);

            SystemLevel<byte> coolant = engineeringSystem.coolant;
            trainerService.monitorProperty(coolant.current);
            IRelativeRotaryEncoder rotaryEncoder = midiController.GetRotaryEncoder(trackId);
            rotaryEncoder.LightPosition.Connect(DerivedProperty<int>.Create(coolant.current, c => c ?? 0));
            rotaryEncoder.Rotated += (_, args) => {
                byte oldCoolantLevel = coolant.current.Value ?? 0;
                byte newCoolantLevel = (byte) Math.Max(Math.Min(oldCoolantLevel + (args.IsClockwise ? 1 : -1), coolant.maximumValue), coolant.minimumValue);
                Console.WriteLine($"Setting {engineeringSystem.name} coolant to {newCoolantLevel:N0}");
                coolant.current.Value = newCoolantLevel;
            };

            trainerService.monitorProperty(engineeringSystem.heat.current);
            trainerService.monitorProperty(engineeringSystem.damage.current);
            trainerService.monitorProperty(engineeringSystem.maxHealth.current);

            midiController.GetScribbleStrip(trackId).BackgroundColor.Connect(DerivedProperty<ScribbleStripBackgroundColor>.Create(engineeringSystem.damage.current, engineeringSystem.maxHealth.current,
                (damage, maxHealth) => {
                    return ((double?) (maxHealth - damage) / maxHealth) switch {
                        >= 1   => ScribbleStripBackgroundColor.Blue,
                        >= 0.5 => ScribbleStripBackgroundColor.Yellow,
                        < 0.5  => ScribbleStripBackgroundColor.Magenta,
                        _      => ScribbleStripBackgroundColor.Blue
                    };
                }));

            midiController.GetVuMeter(trackId).LightPosition.Connect(DerivedProperty<int>.Create(coolant.current, c => c ?? 0));

            IFader fader = midiController.GetFader(trackId);
            fader.ActualPosition.PropertyChanged += (_, args) => { engineeringSystem.power.current.Value = (float) args.NewValue; };
            // fader.DesiredPosition.Connect(DerivedProperty<double>.Create(engineeringSystem.power.current, f => f ?? 0));
        }

        midiController.GetScribbleStrip(ship[EngineeringSystemName.BEAMS].index).TopText.Connect(" Beams ");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.TORPEDOS].index).TopText.Connect("Torpedo");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.SENSORS].index).TopText.Connect("Sensors");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.MANEUVERING].index).TopText.Connect("Maneuv.");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.IMPULSE].index).TopText.Connect("Impulse");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.WARP].index).TopText.Connect(" Warp  ");
        IScribbleStrip frontShieldScribbleStrip = midiController.GetScribbleStrip(ship[EngineeringSystemName.FRONT_SHIELD].index);
        frontShieldScribbleStrip.TopText.Connect(" Front ");
        frontShieldScribbleStrip.BottomText.Connect("Shield ");
        IScribbleStrip rearShieldScribbleStrip = midiController.GetScribbleStrip(ship[EngineeringSystemName.REAR_SHIELD].index);
        rearShieldScribbleStrip.TopText.Connect(" Rear  ");
        rearShieldScribbleStrip.BottomText.Connect("Shield ");

        Game artemis = new ArtemisGame();
        trainerService.attachToGame(artemis);

        
    }

    public void Dispose() {
        trainerService.Dispose();
        midiController.Dispose();
    }

}

public class ArtemisGame: Game {

    public string processName => "Artemis.exe";

    public static IEnumerable<GameVersion> knownVersions { get; } = new List<GameVersion> {
        new(new Version(2, 8, 0), 0x1E2760, "8754EC8D927A62B73DB680A0FF6D3995E7F8B69973FAA1CB87E05D790B31E463"),
        new(new Version(2, 7, 5), 0x1D2F38, "39E7B842CEA2399D3088E93913731DD408C2426DB1973E65ECB918EC0242E05D")
    };

}

public record struct GameVersion(Version version, int baseAddress, string exeSha256HashHex);

internal class ArtemisTrainerService: TrainerServiceImpl {

    protected override Process? findProcess(Game game) {
        //TODO find a more reliable way to differentiate between processes of Engineering and other positions, like Helm
        return SystemWindow.FilterToplevelWindows(window => "Game Window" == window.Title && "Artemis" == window.Process.ProcessName).FirstOrDefault()?.Process;
    }

    protected override Version? getGameVersion(Game game, ProcessHandle processHandle) {
        string           executableAbsolutePath = processHandle.process.MainModule!.FileName;
        SHA256           sha256                 = SHA256.Create();
        using FileStream exeFileStream          = new(executableAbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[]           exeSha256Hash          = sha256.ComputeHash(exeFileStream);

        string exeSha256HashHex = BitConverter.ToString(exeSha256Hash).Replace("-", string.Empty);

        GameVersion gameVersion = ArtemisGame.knownVersions.FirstOrDefault(version => version.exeSha256HashHex == exeSha256HashHex);
        return gameVersion != default ? gameVersion.version : null;
    }

}