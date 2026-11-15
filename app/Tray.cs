using System.Runtime.InteropServices;

namespace Coffres;

public static class Tray
{
    public sealed record Item(string Text, Action Run, bool Checked = false);

    static IntPtr hwnd, icon, bigIcon;
    static uint taskbarCreated;
    static WndProc proc;
    static Func<string> tip = () => "Coffres";
    static Func<IReadOnlyList<Item>> menu = () => Array.Empty<Item>();
    static Action open = () => { };
    static readonly object Gate = new();

    public static void Start(Func<string> tooltip, Func<IReadOnlyList<Item>> items, Action onOpen)
    {
        tip = tooltip; menu = items; open = onOpen;
        var ready = new ManualResetEventSlim();
        var t = new Thread(() => Run(ready)) { IsBackground = true, Name = "Tray" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    static void Run(ManualResetEventSlim ready)
    {
        ExtractIconExW(Environment.ProcessPath ?? "", 0, out bigIcon, out icon, 1);
        proc = WindowProc;
        var wc = new WNDCLASSEXW { cbSize = Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc), hInstance = GetModuleHandleW(null), lpszClassName = "CoffresTray" };
        RegisterClassExW(ref wc);
        hwnd = CreateWindowExW(0, "CoffresTray", "Coffres", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        Add();
        ready.Set();
        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    static NOTIFYICONDATAW Data(uint flags)
    {
        var tooltip = tip();
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = hwnd, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY, hIcon = icon,
            szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip, szInfo = "", szInfoTitle = "",
        };
    }

    static void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIconW(NIM_ADD, ref d);
    }

    public static void Refresh()
    {
        if (hwnd == IntPtr.Zero) return;
        lock (Gate)
        {
            var d = Data(NIF_TIP);
            Shell_NotifyIconW(NIM_MODIFY, ref d);
        }
    }

    public static void Notify(string title, string text)
    {
        if (hwnd == IntPtr.Zero) return;
        lock (Gate)
        {
            var d = Data(NIF_INFO);
            d.szInfoTitle = title.Length > 60 ? title[..60] : title;
            d.szInfo = text.Length > 250 ? text[..250] : text;
            d.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
            d.hBalloonIcon = bigIcon;
            Shell_NotifyIconW(NIM_MODIFY, ref d);
        }
    }

    public static void Remove()
    {
        if (hwnd == IntPtr.Zero) return;
        var d = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref d);
    }

    static IntPtr WindowProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == taskbarCreated && msg != 0)
        {
            Add();
            return IntPtr.Zero;
        }
        if (msg == WM_TRAY)
        {
            var e = (uint)(lParam.ToInt64() & 0xFFFF);
            if (e is WM_LBUTTONUP or WM_LBUTTONDBLCLK) open();
            else if (e is WM_RBUTTONUP or WM_CONTEXTMENU) ShowMenu();
            return IntPtr.Zero;
        }
        return DefWindowProcW(h, msg, wParam, lParam);
    }

    static void ShowMenu()
    {
        var items = menu();
        var m = CreatePopupMenu();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Text == "-") AppendMenuW(m, MF_SEPARATOR, UIntPtr.Zero, null);
            else AppendMenuW(m, MF_STRING | (items[i].Checked ? MF_CHECKED : 0), (UIntPtr)(i + 1), items[i].Text);
        }
        GetCursorPos(out var p);
        SetForegroundWindow(hwnd);
        var id = TrackPopupMenu(m, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, 0, hwnd, IntPtr.Zero);
        PostMessageW(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(m);
        if (id > 0 && id <= items.Count)
        {
            try { items[id - 1].Run(); } catch (Exception ex) { Program.Log("tray: " + ex.Message); }
        }
    }

    const uint WM_TRAY = 0x8001, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    const uint NIIF_USER = 4, NIIF_LARGE_ICON = 0x20, MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20;

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState; public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance;
        public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
}
