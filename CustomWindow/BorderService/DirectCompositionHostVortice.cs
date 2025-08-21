using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using SDColor = System.Drawing.Color;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice;
using VColor = Vortice.Mathematics.Color;

namespace BorderServiceApp;

internal sealed class DirectCompositionHostVortice : IDisposable
{
    private readonly IntPtr _hwnd;
    private bool _disposed;
    private SDColor _color;
    private int _thickness;
    private readonly bool _debug;
    private ID3D11Device? _d3dDevice; private IDXGIDevice? _dxgiDevice;
    private ID2D1Factory1? _d2dFactory; private ID2D1Device? _d2dDevice; private ID2D1DeviceContext? _d2dSharedDc;
    private IDCompositionDevice? _compDevice; private IDCompositionTarget? _target; private IDCompositionVisual? _root; private IDCompositionVisual? _normalLayer; private IDCompositionVisual? _topLayer;
    private readonly Dictionary<string, EdgeGroup> _groups = new();

    private volatile bool _pendingCommit; private System.Threading.Timer? _commitTimer; private readonly object _commitLock = new();
    private const int CommitDelayMs = 12; // throttle window
    private bool _didImmediateFirstCommit;

    private bool _batchMode;
    private int _paintFailCount;
    private const int PaintFailThreshold = 8; // after failing some edges switch strategy
    private IDCompositionSurface? _batchSurface;
    private IDCompositionVisual? _batchVisual;

    // Diagnostics
    private readonly int _initThreadId;

    public DirectCompositionHostVortice(SDColor color,int thickness,bool debug)
    { 
        _color=color; 
        _thickness=thickness;
        _debug=debug;
        _hwnd=CreateHostWindow(); 
        _initThreadId = Environment.CurrentManagedThreadId;
        if(_debug) 
            BsLog.Info("DComp",$"Host hwnd=0x{_hwnd.ToInt64():X} initThread={_initThreadId}"); 
        InitCompositionDevices(); 
        ResizeHostWindow(); 
    }

    public void UpdateColor(SDColor c){ if(c!=_color){ if(_debug) BsLog.Info("DComp",$"Color change {ColorToHex(_color)} -> {ColorToHex(c)}"); _color=c; ForceRepaintAll(); } }
    public void UpdateThickness(int t){ if(t!=_thickness){ if(_debug) BsLog.Info("DComp",$"Thickness change {_thickness}->{t}"); _thickness=t; ForceRepaintAll(); } }
    private void ForceRepaintAll(){ foreach(var g in _groups.Values) g.ForceRepaint(_color,_thickness); ScheduleCommit(); }

    public void Update(List<OverlayLayer.RECT> normalRects,List<OverlayLayer.RECT> topRects)
    {
        if(_compDevice==null) return;
        if(_batchMode){ RenderBatch(normalRects, topRects); ScheduleCommit(); return; }
        var active=new HashSet<string>();
        int i=0; foreach(var r in normalRects){var k="N"+i++; AddOrUpdate(k,r,false); active.Add(k);} i=0; foreach(var r in topRects){var k="T"+i++; AddOrUpdate(k,r,true); active.Add(k);} 
        var remove=new List<string>(); foreach(var k in _groups.Keys) if(!active.Contains(k)) remove.Add(k);
        foreach(var k in remove){ if(_debug) Console.WriteLine("[DComp] Remove group "+k); _groups[k].Dispose(); _groups.Remove(k);} 
        ScheduleCommit();
    }

    private void AddOrUpdate(string key, OverlayLayer.RECT rc, bool top)
    {
        bool created = false;
        if(!_groups.TryGetValue(key,out var grp))
        {
            grp=new EdgeGroup(_compDevice!, top? _topLayer!: _normalLayer!, _d2dFactory, _color,_thickness,_debug,key,this,_initThreadId);
            _groups[key]=grp; created=true; if(_debug) BsLog.Info("DComp",$"New group {key} rect=({rc.Left},{rc.Top},{rc.Right},{rc.Bottom})");
        }
        grp.Update(rc,_color,_thickness);
        if(created && !_didImmediateFirstCommit){ TryImmediateCommit(); }
    }

    private void TryImmediateCommit(){ try{ _compDevice?.Commit(); _didImmediateFirstCommit=true; if(_debug) BsLog.Info("DComp","Immediate first commit"); } catch(Exception ex){ if(_debug) BsLog.Warn("DComp","Immediate commit fail",ex); } }

    private void ScheduleCommit(){ if(_compDevice==null) return; lock(_commitLock){ _pendingCommit=true; if(_commitTimer==null) _commitTimer=new System.Threading.Timer(_=> FlushCommit(),null,CommitDelayMs,Timeout.Infinite); else _commitTimer.Change(CommitDelayMs, Timeout.Infinite); } }
    private void FlushCommit(){ try { lock(_commitLock){ if(!_pendingCommit) return; _pendingCommit=false; } _compDevice?.Commit(); if(_debug) BsLog.Info("DComp","Commit"); } catch(Exception ex){ if(_debug) BsLog.Err("DComp","Commit",ex); } }

    private void InitCompositionDevices()
    {
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out _d3dDevice).CheckError();
            _dxgiDevice=_d3dDevice!.QueryInterface<IDXGIDevice>();
            try 
            { 
                _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded); 
                _d2dDevice=_d2dFactory.CreateDevice(_dxgiDevice); 
                _d2dSharedDc=_d2dDevice.CreateDeviceContext(DeviceContextOptions.None); 
                if(_debug) 
                    BsLog.Info("DComp","D2D device OK"); 
            } 
            catch(Exception ex) 
            { 
                if(_debug) 
                    BsLog.Warn("DComp","D2D init fail",ex); 
                _d2dFactory=null; 
            }
            DComp.DCompositionCreateDevice(_dxgiDevice!, out _compDevice).CheckError();
            _compDevice!.CreateTargetForHwnd(_hwnd,true,out _target).CheckError();
            _compDevice.CreateVisual(out _root).CheckError();
            _compDevice.CreateVisual(out _normalLayer).CheckError();
            _compDevice.CreateVisual(out _topLayer).CheckError();
            _root!.AddVisual(_normalLayer!,false,null).CheckError();
            _root.AddVisual(_topLayer!,false,_normalLayer).CheckError();
            _target!.SetRoot(_root).CheckError();
            _compDevice.Commit().CheckError();
            if(_debug) BsLog.Info("DComp","Composition device + root committed");
        }
        catch(Exception ex)
        {
            if(_debug) BsLog.Err("DComp","Init",ex);
            throw;
        }
    }

    #region Host window helpers
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct WNDCLASSEX{ public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string? lpszMenuName; public string? lpszClassName; public IntPtr hIconSm; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowEx(int ex,string cls,string title,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr inst,IntPtr param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd,int cmd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOACTIVATE = 0x0010; private const uint SWP_SHOWWINDOW = 0x0040; private static readonly IntPtr HWND_TOPMOST = new(-1);
    private IntPtr CreateHostWindow(){ const string cls="DCompOverlayHost_V2"; WNDCLASSEX wc=new(){cbSize=(uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc=_procPtr, hInstance=GetModuleHandle(null), lpszClassName=cls}; RegisterClassEx(ref wc); int ex=0x00080000|0x00000020|0x00000080|0x08000000|0x00000008; IntPtr hwnd=CreateWindowEx(ex,cls,string.Empty,unchecked((int)0x80000000),0,0,1,1,IntPtr.Zero,IntPtr.Zero,wc.hInstance,IntPtr.Zero); if(hwnd==IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed "+Marshal.GetLastWin32Error()); ShowWindow(hwnd,8); return hwnd; }
    private static IntPtr HostWndProc(IntPtr h,uint m,IntPtr w,IntPtr l)=>DefWindowProc(h,m,w,l); private delegate IntPtr WndProcDel(IntPtr h,uint m,IntPtr w,IntPtr l); private static readonly WndProcDel _procDel = HostWndProc; private static readonly IntPtr _procPtr = Marshal.GetFunctionPointerForDelegate(_procDel);
    private void ResizeHostWindow()
    { 
        int vx=GetSystemMetrics(76), vy=GetSystemMetrics(77), vw=GetSystemMetrics(78), vh=GetSystemMetrics(79); 
        if(vw<=0||vh<=0)
        { 
            vw=1920; 
            vh=1080; 
        } 
        SetWindowPos(_hwnd, HWND_TOPMOST, vx, vy, vw, vh, SWP_NOACTIVATE|SWP_SHOWWINDOW); 
        if(_debug) 
            BsLog.Info("DComp",$"Host resize {vx},{vy} {vw}x{vh}"); 
    }
    #endregion

    public void Dispose(){ if(_disposed) return; _disposed=true; FlushCommit(); _commitTimer?.Dispose(); foreach(var g in _groups.Values) g.Dispose(); _target?.Dispose(); _root?.Dispose(); _normalLayer?.Dispose(); _topLayer?.Dispose(); _compDevice?.Dispose(); _d2dSharedDc?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose(); _dxgiDevice?.Dispose(); _d3dDevice?.Dispose(); }

    private void RenderBatch(List<OverlayLayer.RECT> normalRects, List<OverlayLayer.RECT> topRects)
    {
        if (_debug) BsLog.Info("DComp",$"RenderBatch enter normal={normalRects.Count} top={topRects.Count} batchMode={_batchMode} initThread={_initThreadId} curThread={Environment.CurrentManagedThreadId}");
        try
        {
            if (_debug) BsLog.Info("DComp","RenderBatch EnsureBatchSurface");
            EnsureBatchSurface();
            if (_batchSurface == null)
            {
                if (_debug) BsLog.Warn("DComp","RenderBatch _batchSurface null -> return");
                return;
            }
            if (_debug) BsLog.Info("DComp","RenderBatch BeginDraw start (updateRect=null)");
            var dc = _batchSurface.BeginDraw<ID2D1DeviceContext>(null, out var offset);
            if (_debug) BsLog.Info("DComp",$"RenderBatch BeginDraw ok offset=({offset.X},{offset.Y}) iid={typeof(ID2D1DeviceContext).GUID}");
            try
            {
                if (_debug) BsLog.Info("DComp","RenderBatch Clear start");
                dc.Clear(new VColor(0, 0, 0, 0));
                if (_debug) BsLog.Info("DComp",$"RenderBatch Clear done color=transparent");
                var col = new VColor((byte)_color.R, (byte)_color.G, (byte)_color.B, (byte)255);
                if (_debug) BsLog.Info("DComp",$"RenderBatch draw color={ColorToHex(_color)} thickness={_thickness}");
                int idx = 0;
                foreach (var r in normalRects)
                {
                    if (_debug) BsLog.Info("DComp",$"RenderBatch normal[{idx}] ({r.Left},{r.Top},{r.Right},{r.Bottom})");
                    DrawRect(dc, r, col, _thickness);
                    idx++;
                }
                idx = 0;
                foreach (var r in topRects)
                {
                    if (_debug) BsLog.Info("DComp",$"RenderBatch top[{idx}] ({r.Left},{r.Top},{r.Right},{r.Bottom})");
                    DrawRect(dc, r, col, _thickness);
                    idx++;
                }
                if (_debug) BsLog.Info("DComp","RenderBatch drawing complete");
            }
            finally
            {
                if (_debug) BsLog.Info("DComp","RenderBatch EndDraw start");
                try { _batchSurface.EndDraw(); if (_debug) BsLog.Info("DComp","RenderBatch EndDraw ok"); } catch (Exception ex) { if (_debug) BsLog.Warn("DComp","RenderBatch EndDraw fail",ex); }
                try { dc.Dispose(); if (_debug) BsLog.Info("DComp","RenderBatch dc disposed"); } catch (Exception ex) { if (_debug) BsLog.Warn("DComp","RenderBatch dc dispose fail",ex); }
            }
        }
        catch (Exception ex)
        {
            if (_debug) BsLog.Err("DComp","Batch draw fail",ex, ex.HResult);
        }
        finally
        {
            if (_debug) BsLog.Info("DComp","RenderBatch exit");
        }
    }
    private void EnsureBatchSurface()
    {
        if (_batchSurface != null) return;
        if (_compDevice == null) return;
        if (_debug) BsLog.Info("DComp","Switching to batch surface mode");
        _compDevice.CreateVisual(out _batchVisual).CheckError();
        _root!.AddVisual(_batchVisual!, false, null).CheckError();
        int w = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
        int h = System.Windows.Forms.SystemInformation.VirtualScreen.Height;
        _compDevice.CreateSurface((uint)Math.Max(1, w), (uint)Math.Max(1, h), Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out _batchSurface).CheckError();
        if (_debug) BsLog.Info("DComp",$"Batch surface created size={w}x{h}");
        _batchVisual!.SetContent(_batchSurface).CheckError();
    }
    private static void DrawRect(ID2D1DeviceContext dc, OverlayLayer.RECT r, VColor col, int t)
    {
        if (r.Right <= r.Left || r.Bottom <= r.Top) return;
        using var brush = dc.CreateSolidColorBrush(col);
        dc.FillRectangle(new RawRectF(r.Left, r.Top, r.Right, r.Top + t), brush); // top
        dc.FillRectangle(new RawRectF(r.Left, r.Bottom - t, r.Right, r.Bottom), brush); // bottom
        dc.FillRectangle(new RawRectF(r.Left, r.Top, r.Left + t, r.Bottom), brush); // left
        dc.FillRectangle(new RawRectF(r.Right - t, r.Top, r.Right, r.Bottom), brush); // right
    }

    private void RegisterPaintFailure(){ if(_batchMode) return; if(++_paintFailCount >= PaintFailThreshold){ _batchMode=true; foreach(var g in _groups.Values) g.Dispose(); _groups.Clear(); if(_debug) BsLog.Warn("DComp","Enter batch mode due to paint failures"); } }

    private sealed class EdgeGroup : IDisposable
    {
        private readonly IDCompositionDevice _device; private readonly IDCompositionVisual _parent; private readonly ID2D1Factory1? _factory; private SDColor _lastColor; private int _lastT; private bool _disposed; private readonly Edge[] _edges=new Edge[4];
        private readonly bool _debug; private readonly string _key; private readonly int _initThreadId;
        private OverlayLayer.RECT _lastRect;
        private readonly DirectCompositionHostVortice _host;

        public EdgeGroup(IDCompositionDevice device, IDCompositionVisual parent, ID2D1Factory1? factory, SDColor color, int thickness,bool debug,string key, DirectCompositionHostVortice host,int initThreadId){ _device=device; _parent=parent; _factory=factory; _lastColor=color; _lastT=thickness; _debug=debug; _key=key; _host=host; _initThreadId=initThreadId; for(int i=0;i<4;i++){ device.CreateVisual(out var v).CheckError(); parent.AddVisual(v,false,null).CheckError(); _edges[i]=new Edge{Visual=v,Dirty=true,LastColor=color}; } }
        public void Invalidate(){ foreach(ref var e in _edges.AsSpan()) e.Dirty=true; }
        public void Update(OverlayLayer.RECT r, SDColor color, int t)
        { 
            if(_disposed) 
                return; 
            _lastRect = r; 
            bool colorChanged=color!=_lastColor; 
            bool tChanged=t!=_lastT; 
            UpdateEdge(ref _edges[0], r.Left, r.Top, r.Right-r.Left, t, color, colorChanged, tChanged,0); 
            UpdateEdge(ref _edges[1], r.Left, r.Bottom - t, r.Right-r.Left, t, color, colorChanged, tChanged,1);
            UpdateEdge(ref _edges[2], r.Left, r.Top, t, r.Bottom-r.Top, color, colorChanged, tChanged,2); 
            UpdateEdge(ref _edges[3], r.Right - t, r.Top, t, r.Bottom-r.Top, color, colorChanged, tChanged,3); _lastColor=color; _lastT=t; 
        }
        public void ForceRepaint(SDColor color,int t){ if(_disposed) return; for(int i=0;i<_edges.Length;i++){ _edges[i].Dirty=true; } Update(_lastRect,color,t); }
        private void UpdateEdge(ref Edge e,int x,int y,int w,int h, SDColor color,bool colorChanged,bool thicknessChanged,int idx)
        { 
            if(w<=0||h<=0) 
                return; 
            bool recreate = e.Surface==null || e.Dirty || thicknessChanged; e.Dirty=false; 
            if(recreate)
            { 
                e.Surface?.Dispose(); // always create 1x1 surface and scale
                _device.CreateSurface(1,1, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out var surf).CheckError(); 
                if(_debug) BsLog.Info("DComp",$"Edge create surface key={_key} edge={idx} size=1x1 initThread={_initThreadId} curThread={Environment.CurrentManagedThreadId}");
                if(!PaintSurface(surf,color))
                { 
                    if(_debug) 
                        Console.WriteLine($"[DComp] PaintSurface fail (scaled) key={_key} edge={idx}"); 
                }
                e.Surface=surf; 
                e.LastColor=color; 
                e.W=1; 
                e.H=1; 
                e.Visual.SetContent(surf).CheckError(); 
                if(_debug) 
                    BsLog.Info("DComp",$"Edge recreate(scaled) key={_key} edge={idx} size={w}x{h} pos={x},{y}"); 
            }
            else if(colorChanged && e.Surface!=null)
            { 
                PaintSurface(e.Surface,color); e.LastColor=color;
                if(_debug) 
                    BsLog.Info("DComp",$"Edge repaint key={_key} edge={idx}"); 
            }
            var scale = new System.Numerics.Matrix3x2( w,0, 0,h, 0,0 );
            e.Visual.SetTransform(scale).CheckError();
            e.Visual.SetOffsetX(x).CheckError(); e.Visual.SetOffsetY(y).CheckError(); }
        private bool PaintSurface(IDCompositionSurface surface, SDColor color)
        { 
            if(_debug) BsLog.Info("DComp",$"PaintSurface enter key={_key} color={ColorToHex(color)} initThread={_initThreadId} curThread={Environment.CurrentManagedThreadId}");
            try 
            { 
                if(_debug) BsLog.Info("DComp",$"PaintSurface BeginDraw start key={_key} updateRect=null iid={typeof(ID2D1DeviceContext).GUID}");
                var dc = surface.BeginDraw<ID2D1DeviceContext>(null, out var offset); 
                if(_debug) BsLog.Info("DComp",$"PaintSurface BeginDraw ok key={_key} offset=({offset.X},{offset.Y}) thread={Environment.CurrentManagedThreadId}");
                try 
                { 
                    if(_debug) BsLog.Info("DComp",$"PaintSurface Clear start key={_key} targetColor={ColorToHex(color)}"); 
                    dc.Clear(new VColor((byte)color.R, (byte)color.G, (byte)color.B, (byte)255)); 
                    if(_debug) BsLog.Info("DComp",$"PaintSurface Clear done key={_key}");
                } 
                finally 
                { 
                    if(_debug) BsLog.Info("DComp",$"PaintSurface EndDraw start key={_key}");
                    try 
                    { 
                        surface.EndDraw(); 
                        if(_debug) BsLog.Info("DComp",$"PaintSurface EndDraw ok key={_key}");
                    } 
                    catch(Exception exEnd) 
                    { 
                        if(_debug) BsLog.Warn("DComp",$"PaintSurface EndDraw fail key={_key}",exEnd); 
                    } 
                    if(_debug) BsLog.Info("DComp",$"PaintSurface dc dispose start key={_key}");
                    try
                    { 
                        dc.Dispose(); 
                        if(_debug) BsLog.Info("DComp",$"PaintSurface dc dispose ok key={_key}");
                    } 
                    catch(Exception exDisp) 
                    { 
                        if(_debug) BsLog.Warn("DComp",$"PaintSurface dc dispose fail key={_key}",exDisp); 
                    } 
                } 
                if(_debug) BsLog.Info("DComp",$"PaintSurface success key={_key}");
                return true; 
            } 
            catch(Exception ex) 
            { 
                if(_debug) 
                    BsLog.Err("DComp",$"PaintSurface exception key={_key} thread={Environment.CurrentManagedThreadId} iid={typeof(ID2D1DeviceContext).GUID}",ex, ex.HResult); 
                _host.RegisterPaintFailure(); 
                try
                { 
                    surface.EndDraw(); 
                } 
                catch { } 
                if(_debug) BsLog.Info("DComp",$"PaintSurface fail exit key={_key}");
                return false; 
            } 
        }
        public void Dispose(){ if(_disposed) return; _disposed=true; foreach(var ed in _edges){ ed.Surface?.Dispose(); ed.Visual.Dispose(); } }
        private struct Edge{ public IDCompositionVisual Visual; public IDCompositionSurface? Surface; public int W; public int H; public bool Dirty; public SDColor LastColor; }
    }

    private static string ColorToHex(SDColor c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
