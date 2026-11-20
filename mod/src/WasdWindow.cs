// PZA's WASD Control (Meitou). Author: PZA. Licence: GPLv3 or later.
// The window (the user's approved mock-up, 2026-11-17) on PZA's Meitou UI (the shared window library, compiled in): a real Kenshi window with
// three tabs and the tiny button:
//   CONTROL  On / Off; the camera (Normal / Over the shoulder / First person); look speed and the shoulder view's field of view (sliders
//            with - / +, 0.10.1); steer distance; invert look; shoulder side; time under a dial - direct buttons, the
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
    public static string LayoutXml() => L.Doc(L.WindowMin("Root", "PZA'S WASD & CONTROLLER SUPPORT", 0.36, 0.12, 0.28, 0.60,
        L.Tabs("Tabs", 0.03, 0.02, 0.94, 0.96,
            ("TabMain", [
                L.Text("lbState", 0.03, 0.005, 0.94, 0.06),
                L.Button("On", 0.03, 0.068, 0.455, 0.072), L.Button("Off", 0.515, 0.068, 0.455, 0.072),
                L.Text("lbCamera", 0.03, 0.152, 0.94, 0.045),
                L.Button("ViewNormal", 0.03, 0.199, 0.3, 0.065), L.Button("ViewShoulder", 0.35, 0.199, 0.3, 0.065), L.Button("ViewFirst", 0.67, 0.199, 0.3, 0.065),
                L.Text("lbLook", 0.03, 0.272, 0.94, 0.045),
                L.Button("LookLess", 0.03, 0.319, 0.12, 0.065), L.Slider("LookSlider", 0.17, 0.319, 0.66, 0.065, range: LookPositions, pos: LookPos(Driver.Settings.LookScale)), L.Button("LookMore", 0.85, 0.319, 0.12, 0.065),
                L.Text("lbFov", 0.03, 0.392, 0.94, 0.045),
                L.Button("FovLess", 0.03, 0.439, 0.12, 0.065), L.Slider("FovSlider", 0.17, 0.439, 0.66, 0.065, range: FovPositions, pos: FovPos(Driver.Settings.ShoulderFov)), L.Button("FovMore", 0.85, 0.439, 0.12, 0.065),
                L.Text("lbSteer", 0.03, 0.512, 0.94, 0.045),
                L.Button("Steer30", 0.03, 0.559, 0.2175, 0.065), L.Button("Steer40", 0.2708, 0.559, 0.2175, 0.065), L.Button("Steer60", 0.5117, 0.559, 0.2175, 0.065), L.Button("Steer80", 0.7525, 0.559, 0.2175, 0.065),
                L.Text("lbInvert", 0.03, 0.632, 0.94, 0.045),
                L.Button("InvertOff", 0.03, 0.679, 0.455, 0.065), L.Button("InvertOn", 0.515, 0.679, 0.455, 0.065),
                L.Text("lbSide", 0.03, 0.752, 0.94, 0.045),
                L.Button("SideLeft", 0.03, 0.799, 0.455, 0.065), L.Button("SideRight", 0.515, 0.799, 0.455, 0.065),
                L.Text("lbDialTime", 0.03, 0.872, 0.94, 0.045),
                L.Button("DialSlow", 0.03, 0.919, 0.3, 0.065), L.Button("DialPause", 0.35, 0.919, 0.3, 0.065), L.Button("DialNormal", 0.67, 0.919, 0.3, 0.065)]),
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
        PzaWindow.Enable(b, "On", "Off", "ViewNormal", "ViewShoulder", "ViewFirst", "LookLess", "LookMore", "FovLess", "FovMore", "InvertOff", "InvertOn", "Steer30", "Steer40", "Steer60", "Steer80", "DialSlow", "DialPause", "DialNormal",
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
        b.Text["lbLook"] = Strings.Format("win.look", Keys.LookFactor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        b.Text["LookLess"] = Strings.Get("win.less");
        b.Text["LookMore"] = Strings.Get("win.more");
        b.Text["lbFov"] = Strings.Format("win.fov", Math.Round(ViewLens.Lens(s.ShoulderFov) * 180 / Math.PI));
        b.Text["FovLess"] = Strings.Get("win.less");
        b.Text["FovMore"] = Strings.Get("win.more");
        b.Text["lbInvert"] = Strings.Get("win.invert");
        b.Text["InvertOff"] = Pick("win.off", !s.InvertLook);
        b.Text["InvertOn"] = Pick("win.on", s.InvertLook);
        b.Text["lbSide"] = Strings.Get("win.side");
        b.Text["SideLeft"] = Pick("win.left", s.ShoulderSide == "Left");
        b.Text["SideRight"] = Pick("win.right", s.ShoulderSide != "Left");
        b.Text["lbSteer"] = Strings.Format("win.steer", Math.Round(s.SteerDistance));
        b.Text["Steer30"] = Pick("win.steer_short", Math.Abs(s.SteerDistance - 30) < 0.5f);
        b.Text["Steer40"] = Pick("win.steer_normal", Math.Abs(s.SteerDistance - 40) < 0.5f);
        b.Text["Steer60"] = Pick("win.steer_long", Math.Abs(s.SteerDistance - 60) < 0.5f);
        b.Text["Steer80"] = Pick("win.steer_longer", Math.Abs(s.SteerDistance - 80) < 0.5f);
        b.Text["lbDialTime"] = Strings.Get("win.dial_time");
        b.Text["DialSlow"] = Pick("win.dial_slow", s.DialTime == "Slow");
        b.Text["DialPause"] = Pick("win.dial_pause", s.DialTime == "Pause");
        b.Text["DialNormal"] = Pick("win.normal", s.DialTime == "Normal");

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
            case "LookLess": SetLook(LookPos(Driver.Settings.LookScale) - 1, "-"); break;
            case "LookMore": SetLook(LookPos(Driver.Settings.LookScale) + 1, "+"); break;
            case "FovLess": SetFov(FovPos(Driver.Settings.ShoulderFov) - 1, "-"); break;
            case "FovMore": SetFov(FovPos(Driver.Settings.ShoulderFov) + 1, "+"); break;
            case "scroll" when e.Widget == "LookSlider": SetLook((int)Math.Round(e.Value), null); break;
            case "scroll" when e.Widget == "FovSlider": SetFov((int)Math.Round(e.Value), null); break;
            case "Steer30": Driver.SetSetting(nameof(DcSettings.SteerDistance), 30f); break;
            case "Steer40": Driver.SetSetting(nameof(DcSettings.SteerDistance), 40f); break;
            case "Steer60": Driver.SetSetting(nameof(DcSettings.SteerDistance), 60f); break;
            case "Steer80": Driver.SetSetting(nameof(DcSettings.SteerDistance), 80f); break;
            case "DialSlow": Driver.SetSetting(nameof(DcSettings.DialTime), "Slow"); break;
            case "DialPause": Driver.SetSetting(nameof(DcSettings.DialTime), "Pause"); break;
            case "DialNormal": Driver.SetSetting(nameof(DcSettings.DialTime), "Normal"); break;
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

    #region The two sliders (0.10.1): look speed and the over-the-shoulder field of view, the same steps as Settings > Mods

    public static int LookPositions => (int)Math.Round((DcSettings.MaxLook - DcSettings.MinLook) / DcSettings.LookStep) + 1;
    public static int FovPositions => (int)Math.Round((DcSettings.MaxFov - DcSettings.MinFov) / DcSettings.FovStep) + 1;

    /// <summary>The slider position for a setting (out-of-range and odd values clamp), and back.</summary>
    public static int LookPos(double scale) => (int)Math.Round((Keys.Look(scale) - DcSettings.MinLook) / DcSettings.LookStep);
    public static double LookAt(int pos) => Math.Round(DcSettings.MinLook + Math.Clamp(pos, 0, LookPositions - 1) * DcSettings.LookStep, 2);
    public static int FovPos(double degrees) => (int)Math.Round((ViewLens.Lens(degrees) * 180 / Math.PI - DcSettings.MinFov) / DcSettings.FovStep);
    public static double FovAt(int pos) => DcSettings.MinFov + Math.Clamp(pos, 0, FovPositions - 1) * DcSettings.FovStep;

    static int s_sliderLogs;
    static double s_shownLook = double.NaN, s_shownFov = double.NaN;

    /// <summary>The look slider or its - / + moved (button: the slider is moved to match).</summary>
    static void SetLook(int pos, string? button)
    {
        double v = LookAt(pos);
        if (v == Driver.Settings.LookScale) return;

        Driver.SetSetting(nameof(DcSettings.LookScale), v);
        Counters.Add("window.look");
        if (++s_sliderLogs <= 40) Log.Line($"WASD Control: look speed {v:0.00} ({button ?? "the slider"})");
    }

    /// <summary>The field-of-view slider or its - / + moved.</summary>
    static void SetFov(int pos, string? button)
    {
        double v = FovAt(pos);
        if (v == Driver.Settings.ShoulderFov) return;

        Driver.SetSetting(nameof(DcSettings.ShoulderFov), v);
        Counters.Add("window.fov");
        if (++s_sliderLogs <= 40) Log.Line($"WASD Control: over-the-shoulder field of view {v:0} degrees ({button ?? "the slider"})");
    }

    /// <summary>Once a frame: the sliders follow their settings when they changed elsewhere (the - / + buttons, Settings > Mods).</summary>
    static void FollowSliders()
    {
        if (!Window.IsOpen)
        {
            s_shownLook = s_shownFov = double.NaN;
            return;
        }

        var st = Driver.Settings;
        if (st.LookScale != s_shownLook)
        {
            s_shownLook = st.LookScale;
            Window.SetSlider("LookSlider", LookPos(st.LookScale));
        }
        if (st.ShoulderFov != s_shownFov)
        {
            s_shownFov = st.ShoulderFov;
            Window.SetSlider("FovSlider", FovPos(st.ShoulderFov));
        }
    }

    #endregion

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
        FollowSliders();
    }
}
