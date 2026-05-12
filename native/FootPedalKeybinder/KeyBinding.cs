namespace FootPedalKeybinder;

internal sealed record KeyBinding(string Display, byte Modifier, byte Usage);

internal static class KeyBindings
{
    private static readonly Dictionary<Keys, (string Display, byte Usage)> UsageByKey = new()
    {
        [Keys.A] = ("A", 0x04), [Keys.B] = ("B", 0x05), [Keys.C] = ("C", 0x06),
        [Keys.D] = ("D", 0x07), [Keys.E] = ("E", 0x08), [Keys.F] = ("F", 0x09),
        [Keys.G] = ("G", 0x0A), [Keys.H] = ("H", 0x0B), [Keys.I] = ("I", 0x0C),
        [Keys.J] = ("J", 0x0D), [Keys.K] = ("K", 0x0E), [Keys.L] = ("L", 0x0F),
        [Keys.M] = ("M", 0x10), [Keys.N] = ("N", 0x11), [Keys.O] = ("O", 0x12),
        [Keys.P] = ("P", 0x13), [Keys.Q] = ("Q", 0x14), [Keys.R] = ("R", 0x15),
        [Keys.S] = ("S", 0x16), [Keys.T] = ("T", 0x17), [Keys.U] = ("U", 0x18),
        [Keys.V] = ("V", 0x19), [Keys.W] = ("W", 0x1A), [Keys.X] = ("X", 0x1B),
        [Keys.Y] = ("Y", 0x1C), [Keys.Z] = ("Z", 0x1D),
        [Keys.D1] = ("1", 0x1E), [Keys.D2] = ("2", 0x1F), [Keys.D3] = ("3", 0x20),
        [Keys.D4] = ("4", 0x21), [Keys.D5] = ("5", 0x22), [Keys.D6] = ("6", 0x23),
        [Keys.D7] = ("7", 0x24), [Keys.D8] = ("8", 0x25), [Keys.D9] = ("9", 0x26),
        [Keys.D0] = ("0", 0x27),
        [Keys.Return] = ("Enter", 0x28), [Keys.Escape] = ("Escape", 0x29),
        [Keys.Back] = ("Backspace", 0x2A), [Keys.Tab] = ("Tab", 0x2B),
        [Keys.Space] = ("Space", 0x2C), [Keys.OemMinus] = ("-", 0x2D),
        [Keys.Oemplus] = ("=", 0x2E), [Keys.OemOpenBrackets] = ("[", 0x2F),
        [Keys.OemCloseBrackets] = ("]", 0x30), [Keys.OemPipe] = ("\\", 0x31),
        [Keys.OemSemicolon] = (";", 0x33), [Keys.OemQuotes] = ("'", 0x34),
        [Keys.Oemtilde] = ("`", 0x35), [Keys.Oemcomma] = (",", 0x36),
        [Keys.OemPeriod] = (".", 0x37), [Keys.OemQuestion] = ("/", 0x38),
        [Keys.CapsLock] = ("Caps Lock", 0x39),
        [Keys.F1] = ("F1", 0x3A), [Keys.F2] = ("F2", 0x3B), [Keys.F3] = ("F3", 0x3C),
        [Keys.F4] = ("F4", 0x3D), [Keys.F5] = ("F5", 0x3E), [Keys.F6] = ("F6", 0x3F),
        [Keys.F7] = ("F7", 0x40), [Keys.F8] = ("F8", 0x41), [Keys.F9] = ("F9", 0x42),
        [Keys.F10] = ("F10", 0x43), [Keys.F11] = ("F11", 0x44), [Keys.F12] = ("F12", 0x45),
        [Keys.PrintScreen] = ("Print Screen", 0x46), [Keys.Scroll] = ("Scroll Lock", 0x47),
        [Keys.Pause] = ("Pause", 0x48), [Keys.Insert] = ("Insert", 0x49),
        [Keys.Home] = ("Home", 0x4A), [Keys.PageUp] = ("Page Up", 0x4B),
        [Keys.Delete] = ("Delete", 0x4C), [Keys.End] = ("End", 0x4D),
        [Keys.PageDown] = ("Page Down", 0x4E), [Keys.Right] = ("Right", 0x4F),
        [Keys.Left] = ("Left", 0x50), [Keys.Down] = ("Down", 0x51), [Keys.Up] = ("Up", 0x52),
    };

    public static bool TryFromKeyEvent(KeyEventArgs e, out KeyBinding? binding)
    {
        binding = null;
        var key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return false;
        }

        if (!UsageByKey.TryGetValue(key, out var mapped))
        {
            return false;
        }

        byte modifier = 0;
        var labels = new List<string>();
        if (e.Control)
        {
            modifier |= 0x01;
            labels.Add("Ctrl");
        }
        if (e.Shift)
        {
            modifier |= 0x02;
            labels.Add("Shift");
        }
        if (e.Alt)
        {
            modifier |= 0x04;
            labels.Add("Alt");
        }

        labels.Add(mapped.Display);
        binding = new KeyBinding(string.Join("+", labels), modifier, mapped.Usage);
        return true;
    }
}
