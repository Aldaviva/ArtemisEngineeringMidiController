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
using ThrottleDebounce;

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

    private readonly TrainerService                   trainerService = new ArtemisTrainerService();
    private readonly IRelativeBehringerXTouchExtender midiController = BehringerXTouchExtenderFactory.CreateWithRelativeMode();
    private readonly ICollection<IDisposable>         debouncedFuncs = new List<IDisposable>();

    private static readonly object TYPING_LOCK = new();

    public void start() {
        midiController.Open();

        Ship ship = new();

        foreach (EngineeringSystem engineeringSystem in ship.systems) {
            int trackId = engineeringSystem.column;

            trainerService.monitorProperty(engineeringSystem.power.current);

            WritableSystemLevel<byte> coolant = engineeringSystem.coolant;
            trainerService.monitorProperty(coolant.current);
            IRelativeRotaryEncoder rotaryEncoder = midiController.GetRotaryEncoder(trackId);
            DerivedProperty<int>   coolantLight  = DerivedProperty<int>.Create(coolant.current, c => c ?? 0);
            rotaryEncoder.LightPosition.Connect(coolantLight);
            midiController.GetVuMeter(trackId).LightPosition.Connect(coolantLight);
            rotaryEncoder.Rotated += (_, args) => {
                byte oldCoolantLevel = coolant.current.Value ?? 0;
                byte newCoolantLevel = (byte) Math.Max(Math.Min(oldCoolantLevel + (args.IsClockwise ? 1 : -1), coolant.maximum), coolant.minimum);
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

            IFader fader = midiController.GetFader(trackId);
            fader.ActualPosition.PropertyChanged += (_, args) => { engineeringSystem.power.current.Value = (float) args.NewValue; };
            // fader.DesiredPosition.Connect(DerivedProperty<double>.Create(engineeringSystem.power.current, f => f ?? 0));

            IIlluminatedButton                     selectButton              = midiController.GetSelectButton(trackId);
            StoredProperty<IlluminatedButtonState> isSelectButtonIlluminated = new();
            selectButton.IlluminationState.Connect(isSelectButtonIlluminated);
            RateLimitedFunc<IlluminatedButtonState> turnOffSelectButtonAfterDelay =
                Debouncer.Debounce(() => isSelectButtonIlluminated.Value = IlluminatedButtonState.Off, TimeSpan.FromMilliseconds(500));
            debouncedFuncs.Add(turnOffSelectButtonAfterDelay);

            selectButton.IsPressed.PropertyChanged += (_, args) => {
                if (args.NewValue) {
                    isSelectButtonIlluminated.Value = IlluminatedButtonState.On;
                } else if (!args.NewValue && ProcessHandleProvider.processHandle?.mainWindow.HWnd is { } mainWindowHandle) {
                    lock (TYPING_LOCK) {
                        // https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keyup
                        Win32.PostMessage(mainWindowHandle, Win32.WM_KEYUP, Win32.VK_1 + (uint) trackId, 0xC0010001 + ((uint) trackId << 4));
                    }

                    turnOffSelectButtonAfterDelay.Invoke();
                }
            };

            IIlluminatedButton                     recordButton              = midiController.GetRecordButton(trackId);
            StoredProperty<IlluminatedButtonState> isRecordButtonIlluminated = new();
            recordButton.IlluminationState.Connect(isRecordButtonIlluminated);
            RateLimitedFunc<IlluminatedButtonState> turnOffRecordingButtonAfterDelay =
                Debouncer.Debounce(() => isRecordButtonIlluminated.Value = IlluminatedButtonState.Off, TimeSpan.FromMilliseconds(500));
            debouncedFuncs.Add(turnOffRecordingButtonAfterDelay);

            recordButton.IsPressed.PropertyChanged += (_, args) => {
                if (args.NewValue) {
                    isRecordButtonIlluminated.Value = IlluminatedButtonState.On;
                } else if (!args.NewValue && ProcessHandleProvider.processHandle?.mainWindow.HWnd is { } mainWindowHandle) {
                    lock (TYPING_LOCK) {
                        Win32.PostMessage(mainWindowHandle, Win32.WM_KEYDOWN, Win32.VK_SHIFT, 0x002A0001);
                        // might need delays or additional keydown events or wm_char events here
                        Win32.PostMessage(mainWindowHandle, Win32.WM_KEYUP, Win32.VK_1 + (uint) trackId, 0xC0010001 + ((uint) trackId << 4));
                        Win32.PostMessage(mainWindowHandle, Win32.WM_KEYUP, Win32.VK_SHIFT, 0xC02A0001);
                    }

                    turnOffRecordingButtonAfterDelay.Invoke();
                }
            };
        }

        midiController.GetScribbleStrip(ship[EngineeringSystemName.BEAMS].column).TopText.Connect(" Beams ");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.TORPEDOS].column).TopText.Connect("Torpedo");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.SENSORS].column).TopText.Connect("Sensors");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.MANEUVERING].column).TopText.Connect("Maneuv.");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.IMPULSE].column).TopText.Connect("Impulse");
        midiController.GetScribbleStrip(ship[EngineeringSystemName.WARP].column).TopText.Connect(" Warp  ");
        IScribbleStrip frontShieldScribbleStrip = midiController.GetScribbleStrip(ship[EngineeringSystemName.FRONT_SHIELD].column);
        frontShieldScribbleStrip.TopText.Connect(" Front ");
        frontShieldScribbleStrip.BottomText.Connect("Shield ");
        IScribbleStrip rearShieldScribbleStrip = midiController.GetScribbleStrip(ship[EngineeringSystemName.REAR_SHIELD].column);
        rearShieldScribbleStrip.TopText.Connect(" Rear  ");
        rearShieldScribbleStrip.BottomText.Connect("Shield ");

        Game artemis = new ArtemisGame();
        trainerService.attachToGame(artemis);
    }

    public void Dispose() {
        trainerService.Dispose();
        midiController.Dispose();
        foreach (IDisposable debouncedFunc in debouncedFuncs) {
            debouncedFunc.Dispose();
        }

        debouncedFuncs.Clear();
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