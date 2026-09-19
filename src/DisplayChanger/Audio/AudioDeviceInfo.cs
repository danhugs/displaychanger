namespace DisplayChanger.Audio;

public enum AudioFlow
{
    /// <summary>Playback devices (speakers, headphones).</summary>
    Output,
    /// <summary>Recording devices (microphones).</summary>
    Input,
}

/// <summary>An active audio endpoint.</summary>
/// <param name="Id">MMDevice endpoint ID, e.g. <c>{0.0.0.00000000}.{guid}</c>. Stable across reboots.</param>
/// <param name="Name">Friendly name, e.g. "Speakers (Logitech PRO X Wireless Gaming Headset)".</param>
/// <param name="Flow">Output or Input.</param>
/// <param name="IsDefault">True when this is the current default (multimedia role) for its flow.</param>
public sealed record AudioDeviceInfo(string Id, string Name, AudioFlow Flow, bool IsDefault)
{
    public override string ToString() => $"{Name}{(IsDefault ? " [default]" : "")}";
}
