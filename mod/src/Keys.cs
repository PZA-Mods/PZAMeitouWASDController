// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The mod's keys, changeable in the window's Keys tab (user, 2026-11-17: "controls don't feel intuitive"; the approved mock-up): a click on a
// key's button waits for the next letter pressed. A letter the game's own controls use (this install's controls.cfg over Meitou's defaults:
// Bindings), W A S D, or one of the mod's other keys is refused with a line naming what holds it; Esc gives up.
using Meitou.Game.Host;
using Meitou.Game.Host.Input;

namespace PZA.WASDControl.Meitou;

public enum KeyAction
{
    Toggle,
    Shoulder,
    First,
    Take,
}

public static class Keys
{
    /// <summary>The setting holding each action's key.</summary>
    public static string SettingName(KeyAction a) => a switch
    {
        KeyAction.Toggle => nameof(DcSettings.KeyToggle),
        KeyAction.Shoulder => nameof(DcSettings.KeyShoulder),
        KeyAction.First => nameof(DcSettings.KeyFirst),
        _ => nameof(DcSettings.KeyTake),
    };

    static string Letter(KeyAction a) => a switch
    {
        KeyAction.Toggle => Driver.Settings.KeyToggle,
        KeyAction.Shoulder => Driver.Settings.KeyShoulder,
        KeyAction.First => Driver.Settings.KeyFirst,
        _ => Driver.Settings.KeyTake,
    };

    static readonly (KeyAction Action, Key Default)[] Defaults =
        [(KeyAction.Toggle, Key.V), (KeyAction.Shoulder, Key.Z), (KeyAction.First, Key.N), (KeyAction.Take, Key.G)];

    /// <summary>The key an action is on now (its default when the setting is not a letter).</summary>
    public static Key Of(KeyAction a)
    {
        string s = Letter(a);
        return s.Length == 1 && Enum.TryParse<Key>(s, out var k) ? k : Defaults.First(d => d.Action == a).Default;
    }

    /// <summary>The action waiting for its new key (the window's button was clicked), or null.</summary>
    public static KeyAction? Capturing { get; private set; }
    /// <summary>The last refusal or change, shown in the Keys tab.</summary>
    public static string Message { get; private set; } = "";

    public static void StartCapture(KeyAction a)
    {
        Capturing = a;
        Message = Strings.Get("keys.press");
        Counters.Add("keys.capture");
    }

    /// <summary>In the input step while capturing: the next letter becomes the key (checked), Esc gives up. True when a key was taken
    /// for the capture (it then does nothing else this tick).</summary>
    public static bool Capture(TickInput input, InputBindings bindings)
    {
        if (Capturing is not { } action) return false;

        if (input.WasPressed(Key.Escape))
        {
            (Capturing, Message) = (null, Strings.Get("keys.cancelled"));
            return true;
        }

        foreach (char c in DcSettings.Letters)
        {
            var key = Enum.Parse<Key>(c.ToString());
            if (!input.WasPressed(key)) continue;

            Message = Check(action, key, bindings) is { } why ? why : Take(action, key);
            Capturing = null;
            return true;
        }
        return false;
    }

    /// <summary>Why a key cannot be taken, or null when it is free.</summary>
    public static string? Check(KeyAction action, Key key, InputBindings bindings)
    {
        if (key is Key.W or Key.A or Key.S or Key.D) return Strings.Format("keys.refused_move", key);

        foreach (var other in Enum.GetValues<KeyAction>())
        {
            if (other != action && Of(other) == key) return Strings.Format("keys.refused_ours", key, Strings.Get("key." + other.ToString().ToLowerInvariant()));
        }

        foreach (var a in Enum.GetValues<InputAction>())
        {
            if (bindings.Get(a).Any(b => !b.IsMouse && b.Key == key)) return Strings.Format("keys.refused_game", key, a);
        }
        return null;
    }

    static string Take(KeyAction action, Key key)
    {
        Driver.SetSetting(SettingName(action), key.ToString());
        Counters.Add("keys.changed");
        Log.Line(Strings.Format("log.key_changed", Strings.Get("key." + action.ToString().ToLowerInvariant()), key));
        return Strings.Format("keys.changed", Strings.Get("key." + action.ToString().ToLowerInvariant()), key);
    }

    /// <summary>Look speed (the window's Slow / Normal / Fast) as a factor on the mouse and the right stick.</summary>
    public static float LookFactor => Driver.Settings.LookSpeed switch
    {
        "Slow" => 0.6f,
        "Fast" => 1.6f,
        _ => 1f,
    };

    /// <summary>+1, or -1 with Invert look up / down.</summary>
    public static float InvertSign => Driver.Settings.InvertLook ? -1f : 1f;

    /// <summary>+1 over the right shoulder, -1 over the left.</summary>
    public static float ShoulderSign => Driver.Settings.ShoulderSide == "Left" ? -1f : 1f;
}
