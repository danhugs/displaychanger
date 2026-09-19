using System.Drawing;

namespace DisplayChanger.Displays;

/// <summary>A display attached to the desktop.</summary>
/// <param name="DeviceName">GDI device name, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="FriendlyName">Monitor name from EDID (e.g. "Acer X34 P"), or a fallback like "Display 1".</param>
/// <param name="Position">Top-left corner on the virtual desktop, in physical pixels.</param>
/// <param name="Size">Resolution in physical pixels.</param>
/// <param name="IsPrimary">True when this display sits at the virtual-desktop origin.</param>
public sealed record DisplayInfo(string DeviceName, string FriendlyName, Point Position, Size Size, bool IsPrimary)
{
    public override string ToString() =>
        $"{FriendlyName} ({Size.Width}x{Size.Height} @ {Position.X},{Position.Y}){(IsPrimary ? " [primary]" : "")}";
}
