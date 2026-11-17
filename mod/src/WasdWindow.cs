// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The window (the user's approved mock-up, 2026-11-17) on PZA's Meitou UI (the shared window library, compiled in): a real Kenshi window with
// three tabs and the tiny button:
//   CONTROL  On / Off; the camera (Normal / Over the shoulder / First person); look speed; invert look; shoulder side - direct buttons, the
//            current choice marked (PZA UI rule: no cycling buttons)
//   KEYS     each key on a button: click it, press a letter (checked against the game's own controls); the controller layout below
//   HELP     how it all works
// The tiny button (shown while the window is closed): click = WASD Control on / off, + = the window. F10 opens it too. While the full window is
// open the views' mouse-look pauses (Look.Wanted), so its buttons can be clicked.
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using PZA.MeitouUi;
using L = PZA.MeitouUi.PzaLayout;

namespace PZA.WASDControl.Meitou;

public static class WasdWindow
{
    public const string Id = "pza.wasdcontrol";
    public static PzaWindow Window = null!;
    public static PzaTinyButton Tiny = null!;
    static bool s_started;

    /// <summary>The full window is open (mouse-look pauses).</summary>
    public static bool FullOpen => Window is not null && Window.IsOpen;

    static readonly KeyAction[] KeyRows = [KeyAction.Toggle, KeyAction.Shoulder, KeyAction.First, KeyAction.Take];

    // 0.8.4 (PZA's Meitou UI 0.3.0, the user 2026-11-17: windows too big and half empty, more and more pages): three tabs, a smaller window;
    // the controller list moved under the keys; help and controller rows wrapped in the real font at the list's width, so nothing spills.
    public static string LayoutXml() => L.Doc(L.WindowMin("Root", "PZA'S WASD CONTROL", 0.36, 0.14, 0.28, 0.54,
        L.Tabs("Tabs", 0.03, 0.02, 0.94, 0.96,
            ("TabMain", [
                L.Text("lbState", 0.03, 0.01, 0.94, 0.07),
                L.Button("On", 0.03, 0.09, 0.455, 0.085), L.Button("Off", 0.515, 0.09, 0.455, 0.085),
                L.Text("lbCamera", 0.03, 0.20, 0.94, 0.06),
                L.Button("ViewNormal", 0.03, 0.27, 0.30, 0.085), L.Button("ViewShoulder", 0.35, 0.27, 0.30, 0.085),
                L.Button("ViewFirst", 0.67, 0.27, 0.30, 0.085),
                L.Text("lbLook", 0.03, 0.38, 0.94, 0.06),
                L.Button("LookSlow", 0.03, 0.45, 0.30, 0.085), L.Button("LookNormal", 0.35, 0.45, 0.30, 0.085),
                L.Button("LookFast", 0.67, 0.45, 0.30, 0.085),
                L.Text("lbInvert", 0.03, 0.56, 0.94, 0.06),
                L.Button("InvertOff", 0.03, 0.63, 0.455, 0.085), L.Button("InvertOn", 0.515, 0.63, 0.455, 0.085),
                L.Text("lbSide", 0.03, 0.74, 0.94, 0.06),
                L.Button("SideLeft", 0.03, 0.81, 0.455, 0.085), L.Button("SideRight", 0.515, 0.81, 0.455, 0.085)]),
            ("TabKeys", [
                L.Text("lbKeysIntro", 0.03, 0.01, 0.94, 0.11, wrap: true),
                L.Text("lbKeyToggle", 0.03, 0.13, 0.61, 0.075), L.Button("KeyToggle", 0.65, 0.13, 0.32, 0.075),
                L.Text("lbKeyShoulder", 0.03, 0.215, 0.61, 0.075), L.Button("KeyShoulder", 0.65, 0.215, 0.32, 0.075),
                L.Text("lbKeyFirst", 0.03, 0.30, 0.61, 0.075), L.Button("KeyFirst", 0.65, 0.30, 0.32, 0.075),
                L.Text("lbKeyTake", 0.03, 0.385, 0.61, 0.075), L.Button("KeyTake", 0.65, 0.385, 0.32, 0.075),
                L.Text("lbKeyMessage", 0.03, 0.465, 0.94, 0.07, wrap: true),
                L.Text("lbPad", 0.03, 0.54, 0.94, 0.06),
                L.TextList("lstPad", 0.02, 0.61, 0.96, 0.38)]),
            L.HelpList("TabHelp", "lstHelp"))));

    public static void Setup()
    {
        Window = new PzaWindow(Id, LayoutXml)
        {
            Minimizable = true,
            Bind = Bind,
            Event = OnEvent,
            LoadPlace = () => new Vector2((float)Driver.Settings.WindowPlaceX, (float)Driver.Settings.WindowPlaceY),
            SavePlace = p =>
            {
                Driver.SetSetting(nameof(DcSettings.WindowPlaceX), Math.Round((double)p.X, 3));
                Driver.SetSetting(nameof(DcSettings.WindowPlaceY), Math.Round((double)p.Y, 3));
                Counters.Add("window.place_saved");
            },
            OpenChanged = open =>
            {
                if (!Driver.Settings.ShowWindow) return;
                Driver.SetSetting(nameof(DcSettings.Tiny), !open);
                Counters.Add(open ? "window.full" : "window.tiny");
            },
        };
        PzaUi.Add(Window);

        Tiny = new PzaTinyButton("wasd")
        {
            Caption = () => Strings.Get(Driver.On ? "tiny.on" : "tiny.off"),
            Visible = () => Driver.Settings.ShowWindow && !Window.IsOpen,
            Click = () => { Driver.Toggle("tiny button"); Counters.Add("tiny.click"); },
            OpenFull = () => { Window.Open(); Counters.Add("tiny.plus"); },
            LoadPlace = () => new Vector2((float)Driver.Settings.TinyPlaceX, (float)Driver.Settings.TinyPlaceY),
            SavePlace = p =>
            {
                Driver.SetSetting(nameof(DcSettings.TinyPlaceX), Math.Round((double)p.X, 3));
                Driver.SetSetting(nameof(DcSettings.TinyPlaceY), Math.Round((double)p.Y, 3));
                Counters.Add("tiny.moved");
            },
            LoadOpacity = () => Driver.Settings.TinyOpacity,
            SaveOpacity = a => { Driver.SetSetting(nameof(DcSettings.TinyOpacity), a); Counters.Add("tiny.opacity"); },
        };
        PzaUi.Add(Tiny);
    }

    /// <summary>A choice button's text: the current one marked, as the PZA windows do.</summary>
    static string Pick(string key, bool chosen) => chosen ? "> " + Strings.Get(key) + " <" : Strings.Get(key);

    static void Bind(UiBindings b)
    {
        var s = Driver.Settings;
        PzaWindow.Enable(b, "On", "Off", "ViewNormal", "ViewShoulder", "ViewFirst", "LookSlow", "LookNormal", "LookFast", "InvertOff", "InvertOn",
            "SideLeft", "SideRight", "KeyToggle", "KeyShoulder", "KeyFirst", "KeyTake");
        b.Text["Root"] = Strings.Get("win.title");
        PzaWindow.TabText(b, "Tabs", 0, Strings.Get("tab.control"));
        PzaWindow.TabText(b, "Tabs", 1, Strings.Get("tab.keys"));
        PzaWindow.TabText(b, "Tabs", 2, Strings.Get("tab.help"));

        b.Text["lbState"] = Strings.Format(Driver.On ? "win.state_on" : "win.state_off", Keys.Of(KeyAction.Toggle));
        b.Text["On"] = Pick("win.on", Driver.On);
        b.Text["Off"] = Pick("win.off", !Driver.On);
        b.Text["lbCamera"] = Strings.Get("win.camera");
        b.Text["ViewNormal"] = Pick("win.view_normal", Views.Current == View.Normal);
        b.Text["ViewShoulder"] = Pick("win.view_shoulder", Views.Current == View.Shoulder);
        b.Text["ViewFirst"] = Pick("win.view_first", Views.Current == View.First);
        b.Text["lbLook"] = Strings.Get("win.look");
        b.Text["LookSlow"] = Pick("win.slow", s.LookSpeed == "Slow");
        b.Text["LookNormal"] = Pick("win.normal", s.LookSpeed == "Normal");
        b.Text["LookFast"] = Pick("win.fast", s.LookSpeed == "Fast");
        b.Text["lbInvert"] = Strings.Get("win.invert");
        b.Text["InvertOff"] = Pick("win.off", !s.InvertLook);
        b.Text["InvertOn"] = Pick("win.on", s.InvertLook);
        b.Text["lbSide"] = Strings.Get("win.side");
        b.Text["SideLeft"] = Pick("win.left", s.ShoulderSide == "Left");
        b.Text["SideRight"] = Pick("win.right", s.ShoulderSide != "Left");

        b.Text["lbKeysIntro"] = Strings.Get("keys.intro");
        foreach (var a in KeyRows)
        {
            string name = "Key" + a;
            b.Text["lb" + name] = Strings.Get("key." + a.ToString().ToLowerInvariant());
            b.Text[name] = Keys.Capturing == a ? Strings.Get("keys.waiting") : Keys.Of(a).ToString();
            PzaWindow.Tooltip(b, name, Strings.Get("keys.tip"));
        }
        b.Text["lbKeyMessage"] = Keys.Message;
        b.Text["lbPad"] = Strings.Get("tab.pad");
        Window.Paragraphs(b, "lstPad", string.Join("\n", PadLines()));
        Window.Paragraphs(b, "lstHelp", HelpText());
    }

    /// <summary>The controller page: one row per control, "Button - what it does" (lists scroll, so nothing spills out of the window).</summary>
    public static IEnumerable<string> PadLines()
    {
        for (int i = 1; Strings.Has("pad." + i); i++) yield return Strings.Get("pad." + i);
    }

    /// <summary>The help page's text: its paragraphs with a blank line between (PZA's Meitou UI wraps it at the list's real width).</summary>
    public static string HelpText()
    {
        var parts = new List<string>();
        for (int i = 1; Strings.Has("help." + i); i++) parts.Add(Strings.Get("help." + i));
        return string.Join("\n\n", parts);
    }

    static void OnEvent(MyGuiEvent e)
    {
        Counters.Add("event." + e.Action);
        switch (e.Action)
        {
            case "On" when !Driver.On: Driver.Toggle("window"); break;
            case "Off" when Driver.On: Driver.Toggle("window"); break;
            case "ViewNormal": ChooseView(View.Normal); break;
            case "ViewShoulder": ChooseView(View.Shoulder); break;
            case "ViewFirst": ChooseView(View.First); break;
            case "LookSlow": Driver.SetSetting(nameof(DcSettings.LookSpeed), "Slow"); break;
            case "LookNormal": Driver.SetSetting(nameof(DcSettings.LookSpeed), "Normal"); break;
            case "LookFast": Driver.SetSetting(nameof(DcSettings.LookSpeed), "Fast"); break;
            case "InvertOff": Driver.SetSetting(nameof(DcSettings.InvertLook), false); break;
            case "InvertOn": Driver.SetSetting(nameof(DcSettings.InvertLook), true); break;
            case "SideLeft": Driver.SetSetting(nameof(DcSettings.ShoulderSide), "Left"); break;
            case "SideRight": Driver.SetSetting(nameof(DcSettings.ShoulderSide), "Right"); break;
            case "KeyToggle": Keys.StartCapture(KeyAction.Toggle); break;
            case "KeyShoulder": Keys.StartCapture(KeyAction.Shoulder); break;
            case "KeyFirst": Keys.StartCapture(KeyAction.First); break;
            case "KeyTake": Keys.StartCapture(KeyAction.Take); break;
        }
    }

    /// <summary>A camera button: the view needs WASD Control on, so a view button turns it on first.</summary>
    static void ChooseView(View v)
    {
        if (v != View.Normal && !Driver.On) Driver.Toggle("window");
        Views.Set(v, Driver.ShowNotice);
    }

    /// <summary>Once a frame (PzaUi.EachFrame): the first opening as it was left (tiny or full), the window switched off.</summary>
    public static void EachFrame()
    {
        if (!s_started)
        {
            s_started = true;
            if (Driver.Settings.ShowWindow && !Driver.Settings.Tiny) Window.Open();
        }
        if (!Driver.Settings.ShowWindow && Window.IsOpen) Window.Close();
    }
}
