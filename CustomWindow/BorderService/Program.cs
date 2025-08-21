using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SDColor = System.Drawing.Color;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.DXGI;

namespace BorderServiceApp;

internal static class Program
{
    #region WinEvent PInvoke
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);
    #endregion

    [STAThread]
    public static void Main(string[] args)
    {
        int parentPid = 0;
        SDColor borderColor = SDColor.FromArgb(255, 0, 120, 255);
        int thickness = 2;
        bool forceFallback = false;
        bool occlusion = false;
        bool useDcomp = false;
        bool debug = false;
        bool listBorders = false;
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TextInputHost" };

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--parent": if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) parentPid = p; break;
                case "--color":
                    if (i + 1 < args.Length)
                    {
                        var hex = args[++i].TrimStart('#');
                        if (hex.Length == 6 &&
                            int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                            int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                            int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                        {
                            borderColor = SDColor.FromArgb(255, r, g, b);
                        }
                    }
                    break;
                case "--thickness": if (i + 1 < args.Length && int.TryParse(args[++i], out var t)) thickness = Math.Clamp(t, 1, 20); break;
                case "--exclude": if (i + 1 < args.Length) foreach (var pn in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries)) exclude.Add(pn.Trim()); break;
                case "--force-fallback": forceFallback = true; break;
                case "--occlusion": occlusion = true; break;
                case "--dcomp": useDcomp = true; break;
                case "--debug": debug = true; break;
                case "--list-borders": listBorders = true; break;
            }
        }

        Console.WriteLine(
            $"[BS] args mode={(useDcomp ? "DComp" : "Overlay")} debug={debug} listBorders={listBorders} color=#{borderColor.R:X2}{borderColor.G:X2}{borderColor.B:X2} " +
            $"thickness={thickness} occlusion={occlusion} forceFallback={forceFallback} parentPid={parentPid} excl=[{string.Join(',', exclude)}]");

        Process? parent = null;
        if (parentPid > 0) try { parent = Process.GetProcessById(parentPid); } catch { }
        int currentIntegrity = IntegrityHelper.TryGetIntegrityLevel(Environment.ProcessId);

        DirectCompositionHostVortice? dcompHost = null;
        if (useDcomp)
        {
            try
            {
                dcompHost = new DirectCompositionHostVortice(borderColor, thickness, debug);
                Console.WriteLine("[BS] DirectComposition(Vortice) host initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BS-WARN] DirectComposition init failed -> fallback overlay: " + ex.Message);
                useDcomp = false;
            }
        }
        if (useDcomp) OverlayLayer.DiagnosticsDisallow = true;

        OverlayLayer? overlayNormal = null, overlayTop = null;
        if (!useDcomp)
        {
            overlayNormal = new OverlayLayer(borderColor, thickness, topMost: false, forceFallback);
            overlayTop = new OverlayLayer(borderColor, thickness, topMost: true, forceFallback);
        }

        CancellationTokenSource? ctrlCts = null;
        if (useDcomp && dcompHost != null)
        {
            ctrlCts = new CancellationTokenSource();
            StartControlPipe(dcompHost, ctrlCts.Token, parentPid, () => borderColor, c => borderColor = c, () => thickness, t => thickness = t);
        }

        var procCache = new Dictionary<int, (int integrity, string name, DateTime ts)>();
        TimeSpan procTtl = TimeSpan.FromMinutes(5);
        ulong lastNormalHash = 0, lastTopHash = 0; int lastTotal = 0, lastN = 0, lastT = 0, scanLogCounter = 0;
        IntPtr hWinEventHook = IntPtr.Zero; bool pendingScan = true; DateTime lastEventScan = DateTime.MinValue;
        var debounceTimer = new System.Windows.Forms.Timer { Interval = 140 };
        var heartbeatTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        void Cleanup()
        {
            try { debounceTimer.Stop(); heartbeatTimer.Stop(); if (hWinEventHook != IntPtr.Zero) UnhookWinEvent(hWinEventHook); ctrlCts?.Cancel(); } catch { }
            if (useDcomp) dcompHost?.Dispose(); else { overlayNormal?.Dispose(); overlayTop?.Dispose(); }
        }

        WinEventDelegate winEventProc = (hook, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                string? cls = GetClassNameSafe(hwnd);
                if (cls != null && (cls.StartsWith("OverlayBorderRaw_", StringComparison.Ordinal) || cls.StartsWith("DCompOverlayHost_", StringComparison.Ordinal))) return;
                switch (evt)
                {
                    case 0x8000: case 0x8001: case 0x8002: case 0x8003: case 0x800B: pendingScan = true; break;
                }
            }
            catch { }
        };

        hWinEventHook = SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, winEventProc, 0, 0, 0x0000 | 0x0002 | 0x0001);
        if (hWinEventHook == IntPtr.Zero) Console.WriteLine("[BS-WARN] SetWinEventHook failed -> heartbeat only");

        debounceTimer.Tick += (_, _) => { if (!pendingScan) return; if ((DateTime.UtcNow - lastEventScan).TotalMilliseconds < 110) return; pendingScan = false; lastEventScan = DateTime.UtcNow; Scan(); };
        debounceTimer.Start(); heartbeatTimer.Tick += (_, _) => Scan(); heartbeatTimer.Start();
        Console.CancelKeyPress += (_, _) => { Cleanup(); Environment.Exit(0); }; Application.ApplicationExit += (_, _) => Cleanup();

        List<OverlayLayer.RECT> RemoveFullyCovered(List<OverlayLayer.RECT> list)
        {
            var result = new List<OverlayLayer.RECT>(); var cover = new List<OverlayLayer.RECT>();
            foreach (var r in list)
            {
                bool covered = false;
                foreach (var c in cover)
                    if (c.Left <= r.Left && c.Top <= r.Top && c.Right >= r.Right && c.Bottom >= r.Bottom) { covered = true; break; }
                if (!covered) result.Add(r); cover.Add(r);
            }
            return result;
        }

        string? GetClassNameSafe(IntPtr hwnd)
        {
            try { Span<char> buf = stackalloc char[128]; int len = OverlayLayer.GetClassName(hwnd, buf, buf.Length); if (len > 0) return new string(buf[..len]); } catch { }
            return null;
        }

        bool IsDesktop(string? cls) => string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) || string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase);
        bool IsTaskbar(string? cls) => string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) || string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
        bool IsCloaked(IntPtr hwnd) { const int DWMWA_CLOAKED = 14; int cloaked = 0; return OverlayLayer.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0; }

        void Scan()
        {
            try
            {
                if (parent != null && parent.HasExited) { Console.WriteLine("[BS] parent exit -> shutdown"); Cleanup(); Environment.Exit(0); return; }
                var wins = Enumerate();
                var topRects = new List<OverlayLayer.RECT>(); var normalRects = new List<OverlayLayer.RECT>();
                foreach (var w in wins) { if (w.TopMost) topRects.Add(w.Rect); else normalRects.Add(w.Rect); }
                if (occlusion) { normalRects = RemoveFullyCovered(normalRects); topRects = RemoveFullyCovered(topRects); }
                if (normalRects.Count == 0 && wins.Count > 0) normalRects.Add(wins[0].Rect);
                // Safety filter for minimized placeholders
                normalRects.RemoveAll(r => r.Left <= -30000 && r.Top <= -30000);
                topRects.RemoveAll(r => r.Left <= -30000 && r.Top <= -30000);
                ulong nHash = HashRects(normalRects); ulong tHash = HashRects(topRects);
                if (useDcomp) dcompHost!.Update(normalRects, topRects); else { if (nHash != lastNormalHash) { overlayNormal!.Update(normalRects); lastNormalHash = nHash; } if (tHash != lastTopHash) { overlayTop!.Update(topRects); lastTopHash = tHash; } }
                if (listBorders || debug)
                {
                    try
                    {
                        string RectToStr(OverlayLayer.RECT r) => $"({r.Left},{r.Top},{r.Right - r.Left},{r.Bottom - r.Top})";
                        Console.WriteLine($"[BS-BORDERS] normal[{normalRects.Count}]={string.Join(',', normalRects.ConvertAll(RectToStr))} top[{topRects.Count}]={string.Join(',', topRects.ConvertAll(RectToStr))}");
                    }
                    catch { }
                }
                scanLogCounter++; bool changed = wins.Count != lastTotal || normalRects.Count != lastN || topRects.Count != lastT || nHash != lastNormalHash || tHash != lastTopHash;
                if (changed || (scanLogCounter % 240) == 0)
                { Console.WriteLine($"[BS] scan total={wins.Count} n={normalRects.Count} t={topRects.Count} mode={(useDcomp ? "DComp" : "Overlay")} fbForce={forceFallback}"); lastTotal = wins.Count; lastN = normalRects.Count; lastT = topRects.Count; }
            }
            catch (Exception ex) { if ((++scanLogCounter % 240) == 0) Console.WriteLine("[BS-ERR] scan: " + ex.Message); }
        }

        ulong HashRects(List<OverlayLayer.RECT> rects)
        { ulong h = 1469598103934665603UL; foreach (var r in rects) { h ^= (uint)r.Left; h *= 1099511628211UL; h ^= (uint)r.Top; h *= 1099511628211UL; h ^= (uint)r.Right; h *= 1099511628211UL; h ^= (uint)r.Bottom; h *= 1099511628211UL; } h ^= (ulong)rects.Count; h *= 1099511628211UL; return h; }

        List<WinInfo> Enumerate()
        {
            var list = new List<WinInfo>(); IntPtr h = OverlayLayer.GetTopWindow(IntPtr.Zero);
            while (h != IntPtr.Zero)
            {
                if (OverlayLayer.IsWindowVisible(h) && OverlayLayer.GetWindowRect(h, out OverlayLayer.RECT rc))
                {
                    if ((rc.Left <= -30000 && rc.Top <= -30000) || IsIconic(h)) goto next; // skip minimized placeholders
                    int w = rc.Right - rc.Left; int ht = rc.Bottom - rc.Top;
                    if (w > 3 && ht > 3)
                    {
                        uint pid; OverlayLayer.GetWindowThreadProcessId(h, out pid);
                        if (pid != (uint)Environment.ProcessId)
                        {
                            if (!TryGetProcessInfo((int)pid, out var info)) goto next;
                            string name = info.name; if (exclude.Contains(name)) goto next;
                            string? cls = GetClassNameSafe(h); if (IsDesktop(cls) || IsTaskbar(cls)) goto next; if (IsCloaked(h)) goto next;
                            int integrity = info.integrity; if (currentIntegrity > 0 && integrity > currentIntegrity) goto next;
                            int ex = (int)OverlayLayer.GetWindowLongPtr(h, -20); bool topMost = (ex & 0x8) != 0;
                            list.Add(new WinInfo(h, rc, topMost));
                        }
                    }
                }
            next: h = OverlayLayer.GetWindow(h, 2);
            }
            return list;
        }

        bool TryGetProcessInfo(int pid, out (int integrity, string name, DateTime ts) info)
        {
            if (procCache.TryGetValue(pid, out info)) { if (DateTime.UtcNow - info.ts < procTtl) return true; }
            try { var p = Process.GetProcessById(pid); string name = p.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.ProcessName[..^4] : p.ProcessName; int integ = IntegrityHelper.TryGetIntegrityLevel(pid); info = (integ, name, DateTime.UtcNow); procCache[pid] = info; return true; }
            catch { info = default; return false; }
        }

        Application.Run();
    }

    #region Control Pipe
    private static void StartControlPipe(DirectCompositionHostVortice host, CancellationToken token, int parentPid,
        Func<SDColor> getColor, Action<SDColor> setColor, Func<int> getThickness, Action<int> setThickness)
    {
        Task.Run(async () =>
        {
            string pipeName = $"BorderService_{parentPid}_{Environment.ProcessId}";
            Console.WriteLine("[BS] control pipe name=" + pipeName);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server);
                    while (!token.IsCancellationRequested && server.IsConnected)
                    {
                        var line = await reader.ReadLineAsync(); if (line == null) break; line = line.Trim(); if (line.Length == 0) continue;
                        if (line.StartsWith("color", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2)
                            {
                                var hex = parts[1].TrimStart('#');
                                if (hex.Length == 6 && int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                                { var newColor = SDColor.FromArgb(255, r, g, b); setColor(newColor); host.UpdateColor(newColor); Console.WriteLine("[BS] color updated #" + hex); }
                            }
                        }
                        else if (line.StartsWith("thickness", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2 && int.TryParse(parts[1], out var nt)) { nt = Math.Clamp(nt, 1, 20); setThickness(nt); host.UpdateThickness(nt); Console.WriteLine("[BS] thickness updated " + nt); }
                        }
                        else if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("[BS] control pipe exit command"); Environment.Exit(0); }
                        else Console.WriteLine("[BS] unknown cmd: " + line);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.WriteLine("[BS-WARN] pipe err: " + ex.Message); try { await Task.Delay(1000, token); } catch { } }
            }
            Console.WriteLine("[BS] control pipe end");
        }, token);
    }
    #endregion

    private readonly record struct WinInfo(IntPtr Hwnd, OverlayLayer.RECT Rect, bool TopMost);
}

#region OverlayLayer (overlay fallback + D2D/GDI drawing)
internal sealed class OverlayLayer : IDisposable
{
    public static bool DiagnosticsDisallow = false;
    public static bool DisableGdiFallback = true;
    private IntPtr _hwnd; private readonly bool _topMost; private readonly SDColor _color; private readonly int _thickness; private bool _disposed; private OverlayForm? _fallback; private List<RECT> _rects = new();
    private ID2D1Factory? _d2dFactory; private ID2D1HwndRenderTarget? _rt; private ID2D1SolidColorBrush? _brush; private bool _d2dFailed;
    public bool IsRaw => _fallback == null;
    public OverlayLayer(SDColor color, int thickness, bool topMost, bool forceFallback)
    {
        if (DiagnosticsDisallow) throw new InvalidOperationException("OverlayLayer creation disallowed in DComp diagnostic mode");
        _color = color; _thickness = thickness; _topMost = topMost;
        IntPtr? raw = null; if (!forceFallback) raw = TryCreateRaw();
        if (raw.HasValue) { _hwnd = raw.Value; }
        else {
            if (DisableGdiFallback) { DisableGdiFallback = false; }
            if (!DisableGdiFallback) { _hwnd = CreateFallbackInternal(); } else { _hwnd = IntPtr.Zero; }
        }
        if (_hwnd != IntPtr.Zero) { ResizeToVirtual(); Show(); }
        TryInitD2D(); if (_hwnd != IntPtr.Zero) SetLayeredWindowAttributes(_hwnd, 0, 255, 0x2);
    }
    public void Update(IEnumerable<RECT> rects) { _rects = new List<RECT>(rects); if (!DisableGdiFallback && _fallback != null) _fallback.SetRects(_rects, _thickness, _color); else Redraw(); }
    private void Redraw()
    {
        if (_hwnd == IntPtr.Zero || (!DisableGdiFallback && _fallback != null)) return;
        if (_rt != null && !_d2dFailed)
        {
            try { _rt.BeginDraw(); _rt.Clear(new Color4(0,0,0,0)); if (_brush == null) _brush = _rt.CreateSolidColorBrush(new Color4(_color.R/255f,_color.G/255f,_color.B/255f,1f)); var fronts = new List<RECT>(); foreach (var r in _rects){ DrawRectEdgesSkippingCovered(r, fronts); fronts.Add(r);} _rt.EndDraw(); return; }
            catch (Exception ex) { Console.WriteLine("[BS-WARN] D2D draw fail -> switch suppressed GDI: " + ex.Message); _d2dFailed = true; }
        }
        if (DisableGdiFallback) return; using var g = Graphics.FromHwnd(_hwnd); g.Clear(System.Drawing.Color.Transparent); using var pen = new Pen(_color,1); var fs = new List<RECT>(); foreach (var r in _rects){ DrawRectEdgesSkippingCoveredGDI(g, pen, r, fs); fs.Add(r);} }
    private void DrawRectEdgesSkippingCovered(RECT r, List<RECT> fronts)
    { if (_rt == null || _brush == null) return; bool topCovered=EdgeCovered(r.Left,r.Top,r.Right,r.Top,fronts); bool bottomCovered=EdgeCovered(r.Left,r.Bottom-1,r.Right,r.Bottom-1,fronts); bool leftCovered=EdgeCovered(r.Left,r.Top,r.Left,r.Bottom,fronts); bool rightCovered=EdgeCovered(r.Right-1,r.Top,r.Right-1,r.Bottom,fronts); float th=Math.Max(1,_thickness); if(!topCovered)_rt.DrawRectangle(new RectangleF(r.Left,r.Top,r.Right-r.Left,th),_brush,1f); if(!bottomCovered)_rt.DrawRectangle(new RectangleF(r.Left,r.Bottom-th,r.Right-r.Left,th),_brush,1f); if(!leftCovered)_rt.DrawRectangle(new RectangleF(r.Left,r.Top,th,r.Bottom-r.Top),_brush,1f); if(!rightCovered)_rt.DrawRectangle(new RectangleF(r.Right-th,r.Top,th,r.Bottom-r.Top),_brush,1f); }
    private void DrawRectEdgesSkippingCoveredGDI(Graphics g, Pen pen, RECT r, List<RECT> fronts)
    { bool topCovered=EdgeCovered(r.Left,r.Top,r.Right,r.Top,fronts); bool bottomCovered=EdgeCovered(r.Left,r.Bottom-1,r.Right,r.Bottom-1,fronts); bool leftCovered=EdgeCovered(r.Left,r.Top,r.Left,r.Bottom,fronts); bool rightCovered=EdgeCovered(r.Right-1,r.Top,r.Right-1,r.Bottom,fronts); if(!topCovered)g.DrawLine(pen,r.Left,r.Top,r.Right-1,r.Top); if(!bottomCovered)g.DrawLine(pen,r.Left,r.Bottom-1,r.Right-1,r.Bottom-1); if(!leftCovered)g.DrawLine(pen,r.Left,r.Top,r.Left,r.Bottom-1); if(!rightCovered)g.DrawLine(pen,r.Right-1,r.Top,r.Right-1,r.Bottom-1); }
    private bool EdgeCovered(int x1,int y1,int x2,int y2,List<RECT> fronts)
    { foreach(var f in fronts){ if(x1==x2){ if(x1>=f.Left&&x1<f.Right&&y1>=f.Top&&y2<=f.Bottom) return true;} else { if(y1>=f.Top&&y1<f.Bottom&&x1>=f.Left&&x2<=f.Right) return true;} } return false; }
    private IntPtr CreateFallbackInternal(){ _fallback=new OverlayForm(_topMost); _fallback.Show(); return _fallback.Handle; }
    private void ResizeToVirtual(){ int x=GetSystemMetrics(76), y=GetSystemMetrics(77), w=GetSystemMetrics(78), h=GetSystemMetrics(79); if (w<=0||h<=0){ w=1920; h=1080;} if(_fallback!=null) _fallback.Bounds=new Rectangle(x,y,w,h); else if(_hwnd!=IntPtr.Zero) SetWindowPos(_hwnd,_topMost?(IntPtr)(-1):IntPtr.Zero,x,y,w,h,0x0010|0x0040); if(_rt!=null){ try{ _rt.Resize(new SizeI(w,h)); } catch(Exception ex){ Console.WriteLine("[BS-WARN] RT resize fail: "+ex.Message); _d2dFailed=true;} } }
    private void TryInitD2D(){ if(_d2dFailed||_hwnd==IntPtr.Zero) return; try{ GetWindowRect(_hwnd,out RECT rc); int w=Math.Max(1,rc.Right-rc.Left); int h=Math.Max(1,rc.Bottom-rc.Top); _d2dFactory=D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded); var rtProps=new RenderTargetProperties(); var hwndProps=new HwndRenderTargetProperties{ Hwnd=_hwnd, PixelSize=new SizeI(w,h), PresentOptions=PresentOptions.None}; _rt=_d2dFactory.CreateHwndRenderTarget(rtProps,hwndProps); _brush=_rt.CreateSolidColorBrush(new Color4(_color.R/255f,_color.G/255f,_color.B/255f,1f)); Console.WriteLine($"[BS] D2D RT init size={w}x{h}"); } catch(Exception ex){ Console.WriteLine("[BS-WARN] D2D init failed -> GDI suppressed: "+ex.Message); _d2dFailed=true; } }
    private IntPtr? TryCreateRaw(){ string cls=_topMost?"OverlayBorderRaw_TOP":"OverlayBorderRaw_NORM"; lock(_registered){ if(!_registered.Contains(cls)){ var wc=new WNDCLASSEX{ cbSize=(uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc=_wndProc, hInstance=GetModuleHandle(null), lpszClassName=cls }; ushort atom=RegisterClassEx(ref wc); if(atom==0&&Marshal.GetLastWin32Error()!=1410) return null; _registered.Add(cls);} } int exStyle=WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|(_topMost?WS_EX_TOPMOST:0); IntPtr h=CreateWindowEx(exStyle,cls,string.Empty,WS_POPUP,0,0,0,0,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero); if(h!=IntPtr.Zero){ SetLayeredWindowAttributes(h,0,255,0x02); return h;} return null; }
    private void Show(){ if(_fallback!=null) _fallback.TopMost=_topMost; else if(_hwnd!=IntPtr.Zero) ShowWindow(_hwnd,8); }
    public void Dispose(){ if(_disposed) return; _disposed=true; try{ _brush?.Dispose(); } catch{} try{ _rt?.Dispose(); } catch{} try{ _d2dFactory?.Dispose(); } catch{} if(!DisableGdiFallback&&_fallback!=null){ try{ _fallback.Close(); } catch{} } else if(_hwnd!=IntPtr.Zero) DestroyWindow(_hwnd); }
    private static readonly HashSet<string> _registered=new(); private static readonly WndProc _wndProc=WndProcImpl; private delegate IntPtr WndProc(IntPtr h,uint m,IntPtr w,IntPtr l); private static IntPtr WndProcImpl(IntPtr h,uint m,IntPtr w,IntPtr l)=>DefWindowProc(h,m,w,l);
    [StructLayout(LayoutKind.Sequential)] private struct WNDCLASSEX { public uint cbSize; public uint style; public WndProc? lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string? lpszMenuName; public string? lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowEx(int ex,string cls,string title,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr inst,IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd,int cmd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd,int nIndex);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd,out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd,int cmd);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd,Span<char> name,int max);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int pv,int cb);
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED=0x00080000, WS_EX_TRANSPARENT=0x20, WS_EX_TOOLWINDOW=0x80, WS_EX_NOACTIVATE=0x08000000, WS_EX_TOPMOST=0x8; private const uint SWP_NOACTIVATE=0x0010, SWP_SHOWWINDOW=0x0040;
    private sealed class OverlayForm : Form
    { private List<RECT> _rects=new(); private int _th=1; private SDColor _color=SDColor.Red; private readonly bool _topMost; public OverlayForm(bool topMost){ _topMost=topMost; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=topMost; DoubleBuffered=true; BackColor=System.Drawing.Color.Lime; TransparencyKey=BackColor; Enabled=false; }
      protected override CreateParams CreateParams { get { var cp=base.CreateParams; cp.ExStyle|=0x20|0x08000000|0x00080000|0x00000080|(_topMost?0x00000008:0); cp.Style=unchecked((int)0x80000000); return cp; } }
      public void SetRects(List<RECT> rects,int thickness,SDColor color){ _rects=rects; _th=thickness; _color=color; Invalidate(); }
      protected override bool ShowWithoutActivation => true;
      protected override void OnPaint(PaintEventArgs e){ base.OnPaint(e); using var pen=new Pen(_color,1); foreach(var r in _rects){ int w=r.Right-r.Left-1; int h=r.Bottom-r.Top-1; if(w<=0||h<=0) continue; e.Graphics.DrawRectangle(pen,r.Left-Left,r.Top-Top,w,h); if(_th>1) for(int i=1;i<_th;i++) e.Graphics.DrawRectangle(pen,r.Left-Left+i,r.Top-Top+i,w-2*i,h-2*i); } }
    }
}
#endregion

#region IntegrityHelper
internal static class IntegrityHelper
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr proc, uint desired, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buf, uint len, out uint ret);
    [DllImport("advapi32.dll", SetLastError = true)] static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint index);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
    public static int TryGetIntegrityLevel(int pid)
    { IntPtr hp=IntPtr.Zero, tk=IntPtr.Zero; try { hp=OpenProcess(0x1000,false,(uint)pid); if(hp==IntPtr.Zero) return 0; if(!OpenProcessToken(hp,0x8,out tk)) return 0; uint need=0; GetTokenInformation(tk,25,IntPtr.Zero,0,out need); if(need==0) return 0; IntPtr buf=Marshal.AllocHGlobal((int)need); try { if(!GetTokenInformation(tk,25,buf,need,out need)) return 0; IntPtr pSid=Marshal.ReadIntPtr(buf); byte sub=Marshal.ReadByte(GetSidSubAuthorityCount(pSid)); if(sub==0) return 0; IntPtr pRid=GetSidSubAuthority(pSid,(uint)(sub-1)); return Marshal.ReadInt32(pRid); } finally { Marshal.FreeHGlobal(buf);} } catch { return 0; } finally { if(tk!=IntPtr.Zero) CloseHandle(tk); if(hp!=IntPtr.Zero) CloseHandle(hp);} }
}
#endregion
