using System.Runtime.InteropServices;
using static DisplayChanger.Native.CoreAudioInterop;

namespace DisplayChanger.Audio;

/// <summary>Lists active audio endpoints and changes the default playback / recording device.</summary>
public sealed class AudioService
{
    /// <summary>Active endpoints for the given flow, sorted by name.</summary>
    public IReadOnlyList<AudioDeviceInfo> Enumerate(AudioFlow flow)
    {
        var dataFlow = ToDataFlow(flow);
        var enumerator = CreateEnumerator();
        try
        {
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.eMultimedia, out var def) == 0 && def is not null)
            {
                try { def.GetId(out defaultId); }
                finally { Marshal.ReleaseComObject(def); }
            }

            Check(enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var collection), "EnumAudioEndpoints");
            try
            {
                Check(collection.GetCount(out uint count), "GetCount");
                var list = new List<AudioDeviceInfo>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0 || device is null) continue;
                    try
                    {
                        if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) continue;
                        var name = ReadFriendlyName(device) ?? id;
                        list.Add(new AudioDeviceInfo(id, name, flow, string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>Makes the device the default for all three roles (console, multimedia, communications).</summary>
    /// <exception cref="InvalidOperationException">Windows rejected the change.</exception>
    public void SetDefault(AudioDeviceInfo device)
    {
        var (setDefaultEndpoint, policy) = CreatePolicyConfig();
        try
        {
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            {
                int hr = setDefaultEndpoint(device.Id, role);
                if (hr != 0)
                    throw new InvalidOperationException($"Could not set {device.Name} as default ({role}): {Marshal.GetExceptionForHR(hr)?.Message ?? $"HRESULT 0x{hr:X8}"}.");
            }
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    /// <summary>
    /// Moves the default to the next device (alphabetical order, wrapping), skipping any whose ID is in <paramref name="excludedIds"/>.
    /// Returns the new default, or null if nothing changed (no candidates, or only the current default is eligible).
    /// <paramref name="candidates"/> receives the devices that were eligible for cycling, in cycle order, as they were
    /// before the change (so their <see cref="AudioDeviceInfo.IsDefault"/> flags describe the previous default).
    /// </summary>
    public AudioDeviceInfo? CycleNext(AudioFlow flow, ISet<string> excludedIds, out IReadOnlyList<AudioDeviceInfo> candidates)
    {
        var all = Enumerate(flow);
        var eligible = all.Where(d => !excludedIds.Contains(d.Id)).ToList();
        candidates = eligible;
        if (eligible.Count == 0) return null;

        int idx = eligible.FindIndex(d => d.IsDefault);
        var next = eligible[(idx + 1) % eligible.Count];
        if (next.IsDefault) return null;

        SetDefault(next);
        return next;
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store is null) return null;
        try
        {
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.GetString(); }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static EDataFlow ToDataFlow(AudioFlow flow) => flow == AudioFlow.Output ? EDataFlow.eRender : EDataFlow.eCapture;

    private static void Check(int hr, string what)
    {
        if (hr != 0) throw new InvalidOperationException($"{what} failed: {Marshal.GetExceptionForHR(hr)?.Message ?? $"HRESULT 0x{hr:X8}"}");
    }
}
