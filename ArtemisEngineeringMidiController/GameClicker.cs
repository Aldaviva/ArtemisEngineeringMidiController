#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using ArtemisEngineeringMidiController.Trainer;
using ManagedWinapi.Windows;

namespace ArtemisEngineeringMidiController;

public interface GameClicker<T> where T: struct {

    void setSystemLevel(ProcessHandle processHandle, int columnIndex, T? oldValue, T newValue);

}

public abstract class BaseGameClicker {

    private static readonly object CLICKING_LOCK = new();

    protected RECT getColumn(RECT windowClientArea, int uiColumn) {
        RECT controlBox  = new();
        int  columnWidth = (int) ((windowClientArea.Width - 24.0) / Ship.SYSTEM_COUNT);
        controlBox.Left   = uiColumn * columnWidth;
        controlBox.Right  = controlBox.Left + columnWidth;
        controlBox.Bottom = windowClientArea.Height;
        controlBox.Top    = 0;
        return controlBox;
    }

    protected static RECT getWindowClientArea(SystemWindow mainWindow) => mainWindow.ClientRectangle;

    protected static void click(IntPtr gameWindowHandle, Point point) {
        uint coordinates = BitConverter.ToUInt32(BitConverter.GetBytes((short) point.X).Concat(BitConverter.GetBytes((short) point.Y)).ToArray(), 0);

        lock (CLICKING_LOCK) {
            Console.WriteLine($"Clicking in window client area at ({point.X:N0}, {point.Y:N0})");
            Win32.PostMessage(gameWindowHandle, Win32.WM_MOUSEMOVE, 0, coordinates);
            Win32.PostMessage(gameWindowHandle, Win32.WM_LBUTTONDOWN, Win32.MK_LBUTTON, coordinates);
            Thread.Sleep(8); //needed for power but not coolant
            Win32.PostMessage(gameWindowHandle, Win32.WM_LBUTTONUP, 0, coordinates);
        }
    }

}

public abstract class AbstractGameClicker<T>: BaseGameClicker, GameClicker<T> where T: struct {

    public void setSystemLevel(ProcessHandle processHandle, int columnIndex, T? oldValue, T newValue) {
        SystemWindow gameWindow       = processHandle.mainWindow;
        IntPtr       gameWindowHandle = gameWindow.HWnd;

        RECT               windowClientArea = getWindowClientArea(gameWindow);
        RECT               column           = getColumn(windowClientArea, columnIndex);
        IEnumerable<Point> pointsToClick    = getPointsToClick(windowClientArea, column, oldValue, newValue);

        foreach (Point pointToClick in pointsToClick) {
            click(gameWindowHandle, pointToClick);
        }
    }

    protected abstract IEnumerable<Point> getPointsToClick(RECT windowClientArea, RECT column, T? oldValue, T newValue);

}

public class CoolantClicker: AbstractGameClicker<byte> {

    protected override IEnumerable<Point> getPointsToClick(RECT windowClientArea, RECT column, byte? oldValue, byte newValue) {
        ICollection<Point> result = new List<Point>(2);

        double coolantXOnScreen = column.Left + column.Width * 0.0037 + 87.695;
        if (newValue != 0 || oldValue > 1) {
            int minCoolantFromWindowBottom = (int) (windowClientArea.Height * 0.0238 + 25.609);
            int maxCoolantFromWindowBottom = getHighestCoolantY(windowClientArea.Height);
            Point coolantDotCenterOnScreen = new(coolantXOnScreen,
                column.Bottom - minCoolantFromWindowBottom - (maxCoolantFromWindowBottom - minCoolantFromWindowBottom) / (Ship.MAX_COOLANT - 1) * Math.Max(0, newValue - 1));
            result.Add(coolantDotCenterOnScreen);
        }

        if (newValue == 0) {
            Point downArrow = new(coolantXOnScreen, column.Bottom); // you can actually click the down arrow using the lowest pixel in the window
            result.Add(downArrow);
        }

        return result;
    }

    private static int getHighestCoolantY(int windowClientHeight) => (int) Math.Round(windowClientHeight switch {
        <= 800            => 0.3795 * windowClientHeight - 78.794,
        > 800 and < 820   => 181,
        >= 820 and <= 950 => 0.0265 * windowClientHeight + 159.13,
        > 950 and <= 1135 => 0.9025 * windowClientHeight - 675.05,
        > 1135            => 0.3742 * windowClientHeight - 74.844
    });

}

public class PowerClicker: AbstractGameClicker<float> {

    protected override IEnumerable<Point> getPointsToClick(RECT windowClientArea, RECT column, float? oldValue, float newValue) {
        int minPowerFromWindowBottom = getLowestPowerY(windowClientArea.Height);
        int maxPowerFromWindowBottom = getHighestPowerY(windowClientArea.Height);
        return new[] { new Point(column.Left + 50, column.Bottom - minPowerFromWindowBottom - (maxPowerFromWindowBottom - minPowerFromWindowBottom) * newValue) };
    }

    private static int getHighestPowerY(int windowClientHeight) => (int) Math.Round(windowClientHeight switch {
        600  => 198,
        664  => 223,
        720  => 246,
        768  => 266,
        800  => 278,
        864  => 228,
        900  => 228,
        960  => 236,
        1024 => 300,
        1050 => 326,
        1080 => 356,
        1200 => 438,
        1440 => 534,

        <= 800           => 0.4049 * windowClientHeight - 45.244,
        > 800 and <= 950 => 228,
        > 950 and < 1135 => 0.9973 * windowClientHeight - 721.44,
        >= 1135          => 0.4016 * windowClientHeight - 44.312
    });

    private static int getLowestPowerY(int windowClientHeight) => (int) Math.Round(windowClientHeight switch {
        600  => 15,
        664  => 16,
        720  => 18,
        768  => 20,
        800  => 20,
        864  => 22,
        900  => 23,
        960  => 24,
        1024 => 26,
        1050 => 27,
        1080 => 27,
        1200 => 30,
        1440 => 36,

        <= 800            => 0.0299 * windowClientHeight - 3.2435,
        > 800 and < 820   => 21,
        >= 820 and <= 950 => 0.0234 * windowClientHeight + 1.8362,
        > 950 and < 1135  => 0.0284 * windowClientHeight - 3.1749,
        >= 1135           => 0.0237 * windowClientHeight + 1.8034
    });

}