using System.Drawing;
using System.Runtime.InteropServices;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger.Displays;

/// <summary>Enumerates attached displays and changes which one is primary.</summary>
public sealed class DisplayService
{
    /// <summary>Displays currently attached to the desktop, ordered left-to-right (then top-to-bottom).</summary>
    public IReadOnlyList<DisplayInfo> Enumerate()
    {
        var names = DisplayNames.Resolve();
        var list = new List<DisplayInfo>();

        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;

            if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            if ((dd.StateFlags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) continue;

            var dm = DEVMODE.Create();
            if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) continue;

            var position = new Point(dm.dmPositionX, dm.dmPositionY);
            var size = new Size((int)dm.dmPelsWidth, (int)dm.dmPelsHeight);
            var friendly = names.TryGetValue(dd.DeviceName, out var n) ? n : DisplayNames.Fallback(dd.DeviceName);
            var isPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0 || position == Point.Empty;

            list.Add(new DisplayInfo(dd.DeviceName, friendly, position, size, isPrimary));
        }

        return list.OrderBy(d => d.Position.X).ThenBy(d => d.Position.Y).ToList();
    }

    /// <summary>Makes <paramref name="target"/> the primary display, shifting all others so it lands at (0,0).</summary>
    /// <exception cref="InvalidOperationException">Windows rejected the change.</exception>
    public void SetPrimary(DisplayInfo target)
    {
        var displays = Enumerate();
        var current = displays.FirstOrDefault(d => string.Equals(d.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{target.FriendlyName} is no longer attached.");

        if (current.IsPrimary) return;

        int dx = -current.Position.X;
        int dy = -current.Position.Y;

        // Stage the new positions for every display without applying (CDS_NORESET), then commit once.
        // The new primary goes first: Windows expects the CDS_SET_PRIMARY device to be at (0,0) at commit time.
        foreach (var d in displays.OrderByDescending(d => d.DeviceName == current.DeviceName))
        {
            var dm = DEVMODE.Create();
            if (!EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                throw new InvalidOperationException($"Could not read settings for {d.FriendlyName}.");

            dm.dmPositionX += dx;
            dm.dmPositionY += dy;
            dm.dmFields = DM_POSITION;

            uint flags = CDS_UPDATEREGISTRY | CDS_NORESET;
            if (d.DeviceName == current.DeviceName) flags |= CDS_SET_PRIMARY;

            int rc = ChangeDisplaySettingsEx(d.DeviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
            if (rc != DISP_CHANGE_SUCCESSFUL)
                throw new InvalidOperationException($"Could not reposition {d.FriendlyName}: {DescribeDispChange(rc)}.");
        }

        int commit = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (commit != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Could not apply the new layout: {DescribeDispChange(commit)}.");
    }

    /// <summary>
    /// Moves the primary to the next display in left-to-right order, wrapping around.
    /// Returns the new primary, or null if there is only one display (nothing changed).
    /// <paramref name="displays"/> receives the displays in cycle order as they were before the change.
    /// </summary>
    public DisplayInfo? CycleNext(out IReadOnlyList<DisplayInfo> displays)
    {
        displays = Enumerate();
        if (displays.Count < 2) return null;

        int idx = displays.ToList().FindIndex(d => d.IsPrimary);
        var next = displays[(idx + 1) % displays.Count];
        SetPrimary(next);
        return next;
    }
}
