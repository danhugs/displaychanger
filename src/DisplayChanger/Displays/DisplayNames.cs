using System.Runtime.InteropServices;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger.Displays;

/// <summary>Resolves GDI device names (\\.\DISPLAYn) to EDID monitor names via the DisplayConfig API.</summary>
internal static class DisplayNames
{
    /// <summary>Returns a map of GDI device name to friendly monitor name. Never throws; returns an empty map on failure.</summary>
    public static Dictionary<string, string> Resolve()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != ERROR_SUCCESS)
                return result;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ERROR_SUCCESS)
                return result;

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref source) != ERROR_SUCCESS) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref target) != ERROR_SUCCESS) continue;

                var gdiName = source.viewGdiDeviceName;
                var friendly = target.monitorFriendlyDeviceName?.Trim();
                if (!string.IsNullOrEmpty(gdiName) && !string.IsNullOrEmpty(friendly) && !result.ContainsKey(gdiName))
                    result[gdiName] = friendly;
            }
        }
        catch
        {
            // Any interop failure just means we fall back to "Display n" names.
        }
        return result;
    }

    /// <summary>Fallback name derived from the GDI device name: \\.\DISPLAY3 -> "Display 3".</summary>
    public static string Fallback(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"Display {digits}" : deviceName;
    }
}
