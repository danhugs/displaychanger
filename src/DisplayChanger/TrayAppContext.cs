using System.Reflection;
using DisplayChanger.Audio;
using DisplayChanger.Displays;
using DisplayChanger.Windowing;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger;

/// <summary>Owns the tray icon, its menu, the global hotkeys and the wiring between them.</summary>
public sealed class TrayAppContext : ApplicationContext
{
    private const string AppName = "DisplayChanger";
    private const string DisplayHotkeyLabel = "Win+/";
    private const string OutputHotkeyLabel = "Win+]";
    private const string InputHotkeyLabel = "Win+'";
    private const string GatherHotkeyLabel = "Win+[";

    private readonly DisplayService _displays = new();
    private readonly AudioService _audio = new();
    private readonly WindowGatherer _windows = new();
    private readonly Settings _settings;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly HotkeyWindow _hotkeys;
    private readonly OverlayWindow _overlay = new();
    private readonly Icon _trayIcon;

    public TrayAppContext()
    {
        _settings = Settings.Load();
        _trayIcon = LoadTrayIcon();

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();

        _icon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = $"{AppName}  {DisplayHotkeyLabel} display, {OutputHotkeyLabel} output, {InputHotkeyLabel} input, {GatherHotkeyLabel} gather windows",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CycleDisplay();

        _hotkeys = new HotkeyWindow();
        _hotkeys.Register(MOD_WIN, VK_OEM_2, DisplayHotkeyLabel, CycleDisplay);
        _hotkeys.Register(MOD_WIN, VK_OEM_6, OutputHotkeyLabel, () => CycleAudio(AudioFlow.Output));
        _hotkeys.Register(MOD_WIN, VK_OEM_7, InputHotkeyLabel, () => CycleAudio(AudioFlow.Input));
        _hotkeys.Register(MOD_WIN, VK_OEM_4, GatherHotkeyLabel, GatherWindows);

        RebuildMenu();
        HandleFirstRun();
        ReportStartupIssues();
    }

    // ---- Menu ----------------------------------------------------------------------------------

    private void RebuildMenu()
    {
        _menu.SuspendLayout();
        _menu.Items.Clear();

        var displayMenu = BuildDisplaySubmenu(out bool canCycleDisplay);
        var outputMenu = BuildAudioSubmenu(AudioFlow.Output, _settings.ExcludedOutputIds, out bool canCycleOutput);
        var inputMenu = BuildAudioSubmenu(AudioFlow.Input, _settings.ExcludedInputIds, out bool canCycleInput);

        _menu.Items.Add(displayMenu);
        _menu.Items.Add(outputMenu);
        _menu.Items.Add(inputMenu);
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(MakeAction("Next display", DisplayHotkeyLabel, canCycleDisplay, CycleDisplay));
        _menu.Items.Add(MakeAction("Next output", OutputHotkeyLabel, canCycleOutput, () => CycleAudio(AudioFlow.Output)));
        _menu.Items.Add(MakeAction("Next input", InputHotkeyLabel, canCycleInput, () => CycleAudio(AudioFlow.Input)));
        _menu.Items.Add(MakeAction("Gather windows to active display", GatherHotkeyLabel, true, GatherWindows));
        _menu.Items.Add(new ToolStripSeparator());

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartupRegistration.IsEnabled() };
        startup.Click += (_, _) => ToggleStartup();
        _menu.Items.Add(startup);

        var notify = new ToolStripMenuItem("Show on-screen pane") { Checked = _settings.ShowNotifications };
        notify.Click += (_, _) => ToggleNotifications();
        _menu.Items.Add(notify);

        _menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exit);

        _menu.ResumeLayout();
    }

    private ToolStripMenuItem BuildDisplaySubmenu(out bool canCycle)
    {
        IReadOnlyList<DisplayInfo> displays;
        string? error = null;
        try { displays = _displays.Enumerate(); }
        catch (Exception ex) { displays = []; error = ex.Message; }

        var primary = displays.FirstOrDefault(d => d.IsPrimary);
        var root = new ToolStripMenuItem($"Display:  {primary?.FriendlyName ?? "(unknown)"}");

        if (error is not null)
        {
            root.DropDownItems.Add(new ToolStripMenuItem($"Could not list displays: {error}") { Enabled = false });
        }
        else
        {
            foreach (var d in displays)
            {
                var item = new ToolStripMenuItem($"{d.FriendlyName}   {d.Size.Width}×{d.Size.Height}")
                {
                    Checked = d.IsPrimary,
                    ToolTipText = d.DeviceName,
                };
                item.Click += (_, _) => SetPrimaryDisplay(d);
                root.DropDownItems.Add(item);
            }
        }

        canCycle = displays.Count > 1;
        return root;
    }

    private ToolStripMenuItem BuildAudioSubmenu(AudioFlow flow, HashSet<string> excluded, out bool canCycle)
    {
        IReadOnlyList<AudioDeviceInfo> devices;
        string? error = null;
        try { devices = _audio.Enumerate(flow); }
        catch (Exception ex) { devices = []; error = ex.Message; }

        var label = flow == AudioFlow.Output ? "Output" : "Input";
        var current = devices.FirstOrDefault(d => d.IsDefault);
        var root = new ToolStripMenuItem($"{label}:  {current?.Name ?? "(none)"}");

        if (error is not null)
        {
            root.DropDownItems.Add(new ToolStripMenuItem($"Could not list devices: {error}") { Enabled = false });
            canCycle = false;
            return root;
        }

        if (devices.Count == 0)
        {
            root.DropDownItems.Add(new ToolStripMenuItem("No active devices") { Enabled = false });
            canCycle = false;
            return root;
        }

        foreach (var d in devices)
        {
            var item = new ToolStripMenuItem(d.Name)
            {
                Checked = d.IsDefault,
                ToolTipText = excluded.Contains(d.Id) ? "Excluded from cycling" : null,
            };
            item.Click += (_, _) => SetDefaultAudio(d);
            root.DropDownItems.Add(item);
        }

        root.DropDownItems.Add(new ToolStripSeparator());

        var cycleMenu = new ToolStripMenuItem("Include in cycle");
        foreach (var d in devices)
        {
            var toggle = new ToolStripMenuItem(d.Name) { Checked = !excluded.Contains(d.Id), CheckOnClick = true };
            toggle.CheckedChanged += (_, _) =>
            {
                if (toggle.Checked) excluded.Remove(d.Id);
                else excluded.Add(d.Id);
                SaveSettings();
            };
            cycleMenu.DropDownItems.Add(toggle);
        }
        root.DropDownItems.Add(cycleMenu);

        canCycle = devices.Count(d => !excluded.Contains(d.Id)) > 1;
        return root;
    }

    private static ToolStripMenuItem MakeAction(string text, string shortcut, bool enabled, Action action)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut, Enabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Actions -------------------------------------------------------------------------------

    private void CycleDisplay()
    {
        try
        {
            var next = _displays.CycleNext(out var displays);
            var selected = next ?? displays.FirstOrDefault(d => d.IsPrimary);
            ShowDisplayPane(displays, selected, next is null ? "Only one display is connected" : null);
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Warning, force: true);
        }
    }

    private void SetPrimaryDisplay(DisplayInfo target)
    {
        try
        {
            _displays.SetPrimary(target);
            ShowDisplayPane(_displays.Enumerate(), target);
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Warning, force: true);
        }
    }

    private void CycleAudio(AudioFlow flow)
    {
        var excluded = ExcludedIds(flow);
        try
        {
            var next = _audio.CycleNext(flow, excluded, out var candidates);
            var selected = next ?? candidates.FirstOrDefault(d => d.IsDefault);
            string? note = null;
            if (next is null)
                note = candidates.Count == 0 ? "No device is included in the cycle" : "No other device is included in the cycle";
            ShowAudioPane(flow, candidates, selected, note);
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Warning, force: true);
        }
    }

    private void SetDefaultAudio(AudioDeviceInfo device)
    {
        var excluded = ExcludedIds(device.Flow);
        try
        {
            _audio.SetDefault(device);
            // List the cycle set, plus the chosen device if it happens to be excluded from cycling.
            var listed = _audio.Enumerate(device.Flow)
                .Where(d => !excluded.Contains(d.Id) || string.Equals(d.Id, device.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ShowAudioPane(device.Flow, listed, device);
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Warning, force: true);
        }
    }

    private void GatherWindows()
    {
        try
        {
            var result = _windows.GatherToActiveDisplay();
            var displays = _displays.Enumerate();
            var target = displays.FirstOrDefault(d => string.Equals(d.DeviceName, result.TargetDeviceName, StringComparison.OrdinalIgnoreCase));

            string note;
            if (result.Moved == 0 && result.Failed == 0)
                note = "All windows are already on this display";
            else
            {
                note = $"Moved {result.Moved} window{(result.Moved == 1 ? "" : "s")} here";
                if (result.Failed > 0) note += $", {result.Failed} could not be moved";
            }

            ShowDisplayPane(displays, target, note, heading: "Windows");
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Warning, force: true);
        }
    }

    private HashSet<string> ExcludedIds(AudioFlow flow) =>
        flow == AudioFlow.Output ? _settings.ExcludedOutputIds : _settings.ExcludedInputIds;

    // ---- On-screen pane ------------------------------------------------------------------------

    private void ShowDisplayPane(IReadOnlyList<DisplayInfo> displays, DisplayInfo? selected, string? note = null, string heading = "Display")
    {
        if (!_settings.ShowNotifications) return;

        // Two identical monitors share a friendly name; add the GDI number so the rows stay distinguishable.
        bool duplicateNames = displays.Select(d => d.FriendlyName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != displays.Count;
        var entries = displays.Select(d => new OverlayWindow.Entry(
            duplicateNames ? $"{d.FriendlyName}  ({DisplayNumber(d)})" : d.FriendlyName,
            selected is not null && string.Equals(d.DeviceName, selected.DeviceName, StringComparison.OrdinalIgnoreCase))).ToList();

        _overlay.Present(heading, entries, note);
    }

    private void ShowAudioPane(AudioFlow flow, IReadOnlyList<AudioDeviceInfo> devices, AudioDeviceInfo? selected, string? note = null)
    {
        if (!_settings.ShowNotifications) return;

        var entries = devices.Select(d => new OverlayWindow.Entry(
            d.Name,
            selected is not null && string.Equals(d.Id, selected.Id, StringComparison.OrdinalIgnoreCase))).ToList();

        _overlay.Present(flow == AudioFlow.Output ? "Output" : "Input", entries, note);
    }

    private static string DisplayNumber(DisplayInfo d)
    {
        // \\.\DISPLAY3 -> "Display 3"
        var digits = new string(d.DeviceName.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"Display {digits}" : d.DeviceName;
    }

    private void ToggleStartup()
    {
        try
        {
            if (StartupRegistration.IsEnabled()) StartupRegistration.Disable();
            else StartupRegistration.Enable();
        }
        catch (Exception ex)
        {
            Notify($"Could not update startup setting: {ex.Message}", ToolTipIcon.Warning, force: true);
        }
    }

    private void ToggleNotifications()
    {
        _settings.ShowNotifications = !_settings.ShowNotifications;
        if (!_settings.ShowNotifications) _overlay.Dismiss();
        SaveSettings();
    }

    private void ExitApplication()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _hotkeys.Dispose();
        _overlay.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
        ExitThread();
    }

    // ---- Startup / housekeeping ---------------------------------------------------------------

    private void HandleFirstRun()
    {
        if (_settings.FirstRunDone) return;
        try
        {
            StartupRegistration.Enable();
        }
        catch (Exception ex)
        {
            Notify($"Could not register to start with Windows: {ex.Message}", ToolTipIcon.Warning, force: true);
        }
        _settings.FirstRunDone = true;
        SaveSettings();
    }

    private void ReportStartupIssues()
    {
        var failed = _hotkeys.Failed.ToList();
        if (failed.Count > 0)
        {
            var names = string.Join(", ", failed.Select(h => h.Label));
            Notify($"{names} already in use by another app. Use the tray menu instead. ({failed[0].ErrorMessage})",
                ToolTipIcon.Warning, force: true);
        }
        else if (_settings.LoadError is not null)
        {
            Notify($"Settings could not be read, using defaults: {_settings.LoadError}", ToolTipIcon.Warning, force: true);
        }
        else if (StartupRegistration.IsStale())
        {
            // The exe moved since it was registered; repoint the Run entry so it keeps working after sign-in.
            try { StartupRegistration.Enable(); } catch { /* best effort */ }
        }
    }

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Notify($"Could not save settings: {ex.Message}", ToolTipIcon.Warning, force: true); }
    }

    /// <summary>Tray balloon, now used only for warnings and errors; routine switches go through the on-screen pane.</summary>
    private void Notify(string text, ToolTipIcon kind, bool force = false)
    {
        if (!force && !_settings.ShowNotifications) return;
        _icon.BalloonTipTitle = AppName;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = kind;
        _icon.ShowBalloonTip(2000);
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("tray.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch { /* fall through */ }

        try
        {
            var path = Environment.ProcessPath ?? Application.ExecutablePath;
            var associated = Icon.ExtractAssociatedIcon(path);
            if (associated is not null) return associated;
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _hotkeys.Dispose();
            _overlay.Dispose();
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }
}
