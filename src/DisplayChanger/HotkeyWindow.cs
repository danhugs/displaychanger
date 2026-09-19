using System.ComponentModel;
using System.Runtime.InteropServices;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger;

/// <summary>Hidden message-only window that owns the app's global hotkey registrations.</summary>
public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    /// <summary>One registered (or attempted) hotkey.</summary>
    public sealed record Hotkey(int Id, string Label, bool Registered, int Error)
    {
        public string ErrorMessage => Error == 0 ? string.Empty : new Win32Exception(Error).Message;
    }

    private readonly Dictionary<int, (Hotkey Info, Action Action)> _hotkeys = new();
    private int _nextId = 0x4443; // arbitrary base, must stay within 0x0000-0xBFFF
    private bool _disposed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    public IEnumerable<Hotkey> Hotkeys => _hotkeys.Values.Select(v => v.Info);
    public IEnumerable<Hotkey> Failed => Hotkeys.Where(h => !h.Registered);

    /// <summary>Registers a global hotkey. Never throws; inspect the returned record's Registered flag.</summary>
    public Hotkey Register(uint modifiers, uint virtualKey, string label, Action action)
    {
        int id = _nextId++;
        Hotkey info;
        if (RegisterHotKey(Handle, id, modifiers | MOD_NOREPEAT, virtualKey))
            info = new Hotkey(id, label, true, 0);
        else
            info = new Hotkey(id, label, false, Marshal.GetLastWin32Error());

        _hotkeys[id] = (info, action);
        return info;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _hotkeys.TryGetValue(m.WParam.ToInt32(), out var entry) && entry.Info.Registered)
        {
            entry.Action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (info, _) in _hotkeys.Values)
            if (info.Registered) UnregisterHotKey(Handle, info.Id);
        _hotkeys.Clear();
        DestroyHandle();
    }
}
