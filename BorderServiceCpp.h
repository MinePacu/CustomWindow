#pragma once

// Managed-facing declarations only (no Windows headers to avoid name clashes)

namespace BorderServiceCpp {

    public value struct ManagedRect { int Left; int Top; int Right; int Bottom; };

    public delegate void BorderLogHandler(int level, System::String^ message);

    public ref class BorderServiceHost sealed
    {
    public:
        BorderServiceHost(int argbColor, int thickness, bool debug);
        ~BorderServiceHost();
        !BorderServiceHost();

        void Update(array<ManagedRect>^ normalRects, array<ManagedRect>^ topRects);
        void UpdateColor(int argbColor);
        void UpdateThickness(int t);
        void ForceRedraw();
        void RepaintCached();
        void SetLogger(BorderLogHandler^ handler); // level: 0=Info,1=Warn,2=Err
        void SetPartialRedrawRatio(float ratio01); // 0..1 fraction of surface
        void EnableOverlapMerge(bool enable);

    private:
        void Destroy();
        System::IntPtr _nativeCtx; // opaque pointer
        bool _disposed; bool _debug; int _thickness; int _colorARGB; int _initThreadId;
        BorderLogHandler^ _logger; System::Runtime::InteropServices::GCHandle _logHandle;
    };
}
