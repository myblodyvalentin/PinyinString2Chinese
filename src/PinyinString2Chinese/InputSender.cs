using System.Runtime.InteropServices;

namespace PinyinString2Chinese;

internal static class InputSender
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    internal static int InputStructSize => InputSize;

    internal static bool ClearEdit(IntPtr hwnd)
    {
        NativeMethods.SendMessageString(hwnd, NativeMethods.EmReplaceSel, (IntPtr)1, "");
        return true;
    }

    internal static bool Tap(int virtualKey, bool extended = false, int gapMs = 8)
    {
        if (!Send([Key(virtualKey, keyUp: false, extended)]))
            return false;

        if (gapMs > 0)
            Thread.Sleep(gapMs);

        return Send([Key(virtualKey, keyUp: true, extended)]);
    }

    internal static bool Chord(int modifier, int virtualKey)
    {
        // 一次送出整组按键时，目标程序会在 Ctrl 已经松开后才处理 C，于是开头多出一个 c。
        try
        {
            if (!Send([Key(modifier, keyUp: false, extended: false)]))
                return false;

            Thread.Sleep(35);
            if (!Send([Key(virtualKey, keyUp: false, extended: false)]))
                return false;

            Thread.Sleep(55);
            return true;
        }
        finally
        {
            Send([Key(virtualKey, keyUp: true, extended: false)]);
            Thread.Sleep(20);
            Send([Key(modifier, keyUp: true, extended: false)]);
        }
    }

    internal static void ReleaseChordKeys()
    {
        // 不能补发 Alt 松开。系统会把它当成按了一下 Alt，菜单被激活，
        // 接下来的字母就变成快捷键，例如 V 会打开记事本的「查看」。
        ReleaseIfDown('C');
        ReleaseIfDown(NativeMethods.VkControl);
    }

    private static void ReleaseIfDown(int virtualKey)
    {
        if (NativeMethods.IsKeyDown(virtualKey))
            Send([Key(virtualKey, keyUp: true, extended: false)]);
    }

    internal static bool TypePinyin(string keystrokes)
    {
        var capsLock = (NativeMethods.GetKeyState(NativeMethods.VkCapital) & 1) != 0;
        if (capsLock)
            Tap(NativeMethods.VkCapital);

        try
        {
            ReleaseChordKeys();
            var first = true;
            foreach (var ch in keystrokes)
            {
                var gap = first ? 22 : 8;
                first = false;
                if (ch == '\'')
                {
                    if (!Tap(NativeMethods.VkOem7, gapMs: gap))
                        return false;
                }
                else if (ch is >= 'a' and <= 'z')
                {
                    var virtualKey = char.ToUpperInvariant(ch);
                    if (!Tap(virtualKey, gapMs: gap))
                        return false;
                }
                else if (ch is >= '0' and <= '9')
                {
                    if (!Tap(ch, gapMs: gap))
                        return false;
                }

                Thread.Sleep(12);
            }

            return true;
        }
        finally
        {
            if (capsLock)
                Tap(NativeMethods.VkCapital);
        }
    }

    internal static bool TypeUnicode(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t')
                continue;

            var down = Unicode(ch, keyUp: false);
            var up = Unicode(ch, keyUp: true);
            if (!Send([down, up]))
                return false;
        }

        return true;
    }

    internal static bool ModifiersReleased()
    {
        return !NativeMethods.IsKeyDown(NativeMethods.VkShift)
            && !NativeMethods.IsKeyDown(NativeMethods.VkControl)
            && !NativeMethods.IsKeyDown(NativeMethods.VkMenu)
            && !NativeMethods.IsKeyDown(NativeMethods.VkLwin)
            && !NativeMethods.IsKeyDown(NativeMethods.VkRwin);
    }

    internal static bool WaitUntilModifiersReleased(int timeoutMs)
    {
        var started = Environment.TickCount64;
        while (!ModifiersReleased())
        {
            if (Environment.TickCount64 - started > timeoutMs)
                return false;

            Thread.Sleep(15);
        }

        return true;
    }

    private static bool Send(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        return sent == inputs.Length;
    }

    private static NativeMethods.INPUT Key(int virtualKey, bool keyUp, bool extended)
    {
        var flags = 0u;
        if (keyUp)
            flags |= NativeMethods.KeyeventfKeyup;
        if (extended || virtualKey == NativeMethods.VkDelete)
            flags |= NativeMethods.KeyeventfExtendedKey;

        return new NativeMethods.INPUT
        {
            Type = NativeMethods.InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)NativeMethods.MapVirtualKey((uint)virtualKey, 0),
                    Flags = flags,
                },
            },
        };
    }

    private static NativeMethods.INPUT Unicode(char ch, bool keyUp)
    {
        var flags = NativeMethods.KeyeventfUnicode;
        if (keyUp)
            flags |= NativeMethods.KeyeventfKeyup;

        return new NativeMethods.INPUT
        {
            Type = NativeMethods.InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    ScanCode = ch,
                    Flags = flags,
                },
            },
        };
    }
}
