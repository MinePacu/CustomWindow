#include "pch.h"
#include "BorderServiceCpp.h"
#include <stdexcept>
#include <vector>
#include <mutex>
#include <algorithm>

#pragma managed(push, off)
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d2d1_1.h>
#include <dcomp.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "dxguid.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dcomp.lib")
#pragma comment(lib, "ole32.lib")

struct BS_NativeRect { int Left; int Top; int Right; int Bottom; };
struct BS_CachedSet { std::vector<BS_NativeRect> normal; std::vector<BS_NativeRect> top; bool dirty=false; };
using BS_LogFn = void (__stdcall *)(int level, const wchar_t* msg);

struct BS_NativeContext {
    HWND hwnd{}; bool comInit{}; bool debug{};
    ID3D11Device* d3d{}; IDXGIDevice* dxgi{}; ID2D1Factory1* d2dFactory{}; ID2D1Device* d2dDevice{}; ID2D1DeviceContext* d2dDc{};
    IDCompositionDevice* comp{}; IDCompositionTarget* target{}; IDCompositionVisual* root{}; IDCompositionVisual* normalLayer{}; IDCompositionVisual* topLayer{};
    IDCompositionSurface* batchSurface{}; IDCompositionVisual* batchVisual{}; UINT surfaceW{}; UINT surfaceH{};
    int colorARGB{}; int thickness{}; BS_CachedSet cache; BS_LogFn logger{}; std::mutex mtx; int consecutiveBeginFail{};
    float partialRatio = 0.25f; bool mergeOverlap = true; // tuning
};

static void BS_Log(BS_NativeContext* ctx, int level, const wchar_t* msg){ if(ctx && ctx->logger) ctx->logger(level, msg); }
static void BS_SafeRelease(IUnknown*& p){ if(p){ p->Release(); p=nullptr; } }

static HWND BS_CreateHostWindow(){
    const wchar_t* CLS=L"BS_DCompHost_Native_CppCLI_Adv"; WNDCLASSEXW wc{}; wc.cbSize=sizeof(wc); wc.lpfnWndProc=DefWindowProcW; wc.hInstance=GetModuleHandleW(nullptr); wc.lpszClassName=CLS; RegisterClassExW(&wc);
    HWND h=CreateWindowExW(WS_EX_NOACTIVATE|WS_EX_LAYERED|WS_EX_TRANSPARENT, CLS,L"",WS_POPUP,0,0,1,1,nullptr,nullptr,wc.hInstance,nullptr);
    if(!h) throw std::runtime_error("CreateWindowEx failed"); ShowWindow(h,SW_SHOWNA); return h; }

static void BS_InitDevices(BS_NativeContext* ctx){
    if(SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) ctx->comInit=true;
    UINT flags=D3D11_CREATE_DEVICE_BGRA_SUPPORT; D3D_FEATURE_LEVEL fl; ID3D11Device* dev=nullptr; ID3D11DeviceContext* imm=nullptr;
    if(FAILED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&dev,&fl,&imm))) throw std::runtime_error("D3D11CreateDevice failed");
    ctx->d3d=dev; dev->QueryInterface(__uuidof(IDXGIDevice),(void**)&ctx->dxgi);
    D2D1_FACTORY_OPTIONS opts{}; if(FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,__uuidof(ID2D1Factory1),&opts,(void**)&ctx->d2dFactory))) throw std::runtime_error("D2D1CreateFactory failed");
    if(FAILED(ctx->d2dFactory->CreateDevice(ctx->dxgi,&ctx->d2dDevice))) throw std::runtime_error("CreateDevice D2D failed");
    if(FAILED(ctx->d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&ctx->d2dDc))) throw std::runtime_error("CreateDeviceContext failed");
    if(FAILED(DCompositionCreateDevice(ctx->dxgi,__uuidof(IDCompositionDevice),(void**)&ctx->comp))) throw std::runtime_error("DCompositionCreateDevice failed");
    if(FAILED(ctx->comp->CreateTargetForHwnd(ctx->hwnd,TRUE,&ctx->target))) throw std::runtime_error("CreateTargetForHwnd failed");
    ctx->comp->CreateVisual(&ctx->root); ctx->comp->CreateVisual(&ctx->normalLayer); ctx->comp->CreateVisual(&ctx->topLayer);
    ctx->root->AddVisual(ctx->normalLayer,FALSE,nullptr); ctx->root->AddVisual(ctx->topLayer,FALSE,ctx->normalLayer);
    ctx->target->SetRoot(ctx->root); ctx->comp->Commit();
}

static void BS_Destroy(BS_NativeContext* ctx){
    if(!ctx) return;
    BS_SafeRelease((IUnknown*&)ctx->batchSurface); BS_SafeRelease((IUnknown*&)ctx->batchVisual);
    BS_SafeRelease((IUnknown*&)ctx->topLayer); BS_SafeRelease((IUnknown*&)ctx->normalLayer); BS_SafeRelease((IUnknown*&)ctx->root);
    BS_SafeRelease((IUnknown*&)ctx->target); BS_SafeRelease((IUnknown*&)ctx->comp);
    BS_SafeRelease((IUnknown*&)ctx->d2dDc); BS_SafeRelease((IUnknown*&)ctx->d2dDevice); BS_SafeRelease((IUnknown*&)ctx->d2dFactory);
    BS_SafeRelease((IUnknown*&)ctx->dxgi); BS_SafeRelease((IUnknown*&)ctx->d3d);
    if(ctx->hwnd) DestroyWindow(ctx->hwnd);
    if(ctx->comInit) CoUninitialize();
}

static void BS_EnsureBatchSurface(BS_NativeContext* ctx){
    int vw=GetSystemMetrics(SM_CXVIRTUALSCREEN); int vh=GetSystemMetrics(SM_CYVIRTUALSCREEN);
    if(vw<=0||vh<=0){ vw=1920; vh=1080; }
    if(ctx->batchSurface && ctx->surfaceW==(UINT)vw && ctx->surfaceH==(UINT)vh) return;
    BS_SafeRelease((IUnknown*&)ctx->batchSurface); BS_SafeRelease((IUnknown*&)ctx->batchVisual);
    ctx->comp->CreateVisual(&ctx->batchVisual);
    ctx->root->AddVisual(ctx->batchVisual,FALSE,nullptr);
    if(FAILED(ctx->comp->CreateSurface(vw,vh,DXGI_FORMAT_B8G8R8A8_UNORM,DXGI_ALPHA_MODE_PREMULTIPLIED,&ctx->batchSurface)))
        throw std::runtime_error("CreateSurface failed");
    ctx->batchVisual->SetContent(ctx->batchSurface);
    ctx->surfaceW=vw; ctx->surfaceH=vh; ctx->comp->Commit();
}

static void BS_MergeRect(std::vector<BS_NativeRect>& out, const BS_NativeRect& r){
    if(r.Right<=r.Left||r.Bottom<=r.Top) return;
    for(auto& o:out){
        if(!(r.Right<o.Left || r.Left>o.Right || r.Bottom<o.Top || r.Top>o.Bottom)){ // overlap -> merge
            if(o.Left > r.Left) o.Left = r.Left;
            if(o.Top > r.Top) o.Top = r.Top;
            if(o.Right < r.Right) o.Right = r.Right;
            if(o.Bottom < r.Bottom) o.Bottom = r.Bottom;
            return;
        }
    }
    out.push_back(r);
}

static void BS_Flatten(const std::vector<BS_NativeRect>& srcN,const std::vector<BS_NativeRect>& srcT,std::vector<BS_NativeRect>& merged,bool merge){
    if(!merge){ merged.reserve(srcN.size()+srcT.size()); merged.insert(merged.end(),srcN.begin(),srcN.end()); merged.insert(merged.end(),srcT.begin(),srcT.end()); return; }
    for(auto& r:srcN) BS_MergeRect(merged,r);
    for(auto& r:srcT) BS_MergeRect(merged,r);
}

static RECT BS_CombineDirty(const BS_CachedSet& oldSet, const BS_CachedSet& newSet, bool& any){
    RECT dirty{LONG_MAX,LONG_MAX,0,0}; any=false;
    auto accumulate=[&](const std::vector<BS_NativeRect>& a, const std::vector<BS_NativeRect>& b){
        size_t na=a.size(), nb=b.size(); size_t n = (na>nb?na:nb);
        for(size_t i=0;i<n;i++){
            const BS_NativeRect* pa = i<na? &a[i]: nullptr;
            const BS_NativeRect* pb = i<nb? &b[i]: nullptr;
            bool diff=false;
            if(!pa || !pb) diff=true; else if(pa->Left!=pb->Left||pa->Top!=pb->Top||pa->Right!=pb->Right||pa->Bottom!=pb->Bottom) diff=true;
            if(diff){ any=true; const BS_NativeRect* use = pb? pb: pa; if(!use) continue; if(use->Right<=use->Left||use->Bottom<=use->Top) continue; if(dirty.left>use->Left) dirty.left=use->Left; if(dirty.top>use->Top) dirty.top=use->Top; if(dirty.right<use->Right) dirty.right=use->Right; if(dirty.bottom<use->Bottom) dirty.bottom=use->Bottom; }
        }
    };
    accumulate(oldSet.normal,newSet.normal);
    accumulate(oldSet.top,newSet.top);
    if(!any || dirty.left==LONG_MAX) { dirty={0,0,0,0}; any=false; }
    return dirty;
}

static HRESULT BS_Begin(IDCompositionSurface* surf, const RECT* upd, ID2D1DeviceContext** outDc, POINT* offset){
    return surf->BeginDraw(upd,__uuidof(ID2D1DeviceContext),(void**)outDc,offset);
}

static void BS_DrawAll(BS_NativeContext* ctx, bool partial, const RECT& upd){
    BS_EnsureBatchSurface(ctx);
    if(!ctx->batchSurface) return;
    RECT* pUpd = nullptr; RECT updCopy{};
    if(partial){ updCopy=upd; pUpd=&updCopy; }
    POINT off{}; ID2D1DeviceContext* dc=nullptr; HRESULT hr=BS_Begin(ctx->batchSurface, pUpd, &dc, &off);
    if(FAILED(hr)){
        ctx->consecutiveBeginFail++;
        BS_Log(ctx,2,L"BeginDraw failed");
        if(ctx->consecutiveBeginFail>2){ // recreate surface fallback
            BS_SafeRelease((IUnknown*&)ctx->batchSurface); BS_SafeRelease((IUnknown*&)ctx->batchVisual); ctx->surfaceW=ctx->surfaceH=0; ctx->consecutiveBeginFail=0; BS_EnsureBatchSurface(ctx);
        }
        return;
    }
    ctx->consecutiveBeginFail=0;
    // If partial, just clear that region by drawing transparent rect (since entire surface premultiplied)
    if(partial){
        D2D1_RECT_F rf{(FLOAT)upd.left,(FLOAT)upd.top,(FLOAT)upd.right,(FLOAT)upd.bottom};
        ID2D1SolidColorBrush* clearBrush=nullptr; dc->CreateSolidColorBrush(D2D1::ColorF(0,0,0,0),&clearBrush);
        dc->FillRectangle(rf, clearBrush); if(clearBrush) clearBrush->Release();
    } else {
        dc->Clear(D2D1::ColorF(0,0));
    }
    float a=((ctx->colorARGB>>24)&0xFF)/255.0f; float r=((ctx->colorARGB>>16)&0xFF)/255.0f; float g=((ctx->colorARGB>>8)&0xFF)/255.0f; float b=(ctx->colorARGB&0xFF)/255.0f;
    ID2D1SolidColorBrush* brush=nullptr; dc->CreateSolidColorBrush(D2D1::ColorF(r,g,b,a),&brush);
    int t=ctx->thickness>0?ctx->thickness:1;
    auto draw=[&](const BS_NativeRect& rr){ if(rr.Right<=rr.Left||rr.Bottom<=rr.Top) return; FLOAT L=(FLOAT)rr.Left,T=(FLOAT)rr.Top,R=(FLOAT)rr.Right,B=(FLOAT)rr.Bottom,TT=(FLOAT)t; dc->FillRectangle(D2D1::RectF(L,T,R,T+TT),brush); dc->FillRectangle(D2D1::RectF(L,B-TT,R,B),brush); dc->FillRectangle(D2D1::RectF(L,T,L+TT,B),brush); dc->FillRectangle(D2D1::RectF(R-TT,T,R,B),brush); };
    for(auto& rct:ctx->cache.normal) draw(rct);
    for(auto& rct:ctx->cache.top) draw(rct);
    if(brush) brush->Release();
    ctx->batchSurface->EndDraw();
    ctx->comp->Commit();
    ctx->cache.dirty=false;
}

extern "C" {
    __declspec(dllexport) BS_NativeContext* BS_CreateContext(int argb,int thickness,int debug){ BS_NativeContext* ctx=new BS_NativeContext(); ctx->debug=debug!=0; ctx->colorARGB=argb; ctx->thickness=thickness; ctx->hwnd=BS_CreateHostWindow(); BS_InitDevices(ctx); return ctx; }
    __declspec(dllexport) void BS_DestroyContext(BS_NativeContext* ctx){ BS_Destroy(ctx); delete ctx; }
    __declspec(dllexport) void BS_UpdateColor(BS_NativeContext* ctx,int argb){ if(ctx){ ctx->colorARGB=argb; ctx->cache.dirty=true; BS_DrawAll(ctx,false,RECT{});} }
    __declspec(dllexport) void BS_UpdateThickness(BS_NativeContext* ctx,int t){ if(ctx){ ctx->thickness=t; ctx->cache.dirty=true; BS_DrawAll(ctx,false,RECT{});} }
    __declspec(dllexport) void BS_UpdateRects(BS_NativeContext* ctx, BS_NativeRect* normal,int normalCount, BS_NativeRect* top,int topCount){ if(!ctx) return; std::lock_guard<std::mutex> lg(ctx->mtx); BS_CachedSet old=ctx->cache; ctx->cache.normal.assign(normal, normal+normalCount); ctx->cache.top.assign(top, top+topCount); ctx->cache.dirty=true; bool any=false; RECT dirty{0,0,0,0}; if(ctx->surfaceW>0 && ctx->surfaceH>0){ // compute dirty only when surface known
            dirty = [&](){ RECT r= {0,0,0,0}; bool a=false; r = BS_CombineDirty(old, ctx->cache, a); any=a; return r; }(); }
        bool doPartial = any && ((dirty.right-dirty.left)*(dirty.bottom-dirty.top)) < (int)(ctx->partialRatio * ctx->surfaceW * ctx->surfaceH);
        BS_DrawAll(ctx, doPartial, dirty); }
    __declspec(dllexport) void BS_ForceRedraw(BS_NativeContext* ctx){ if(!ctx) return; std::lock_guard<std::mutex> lg(ctx->mtx); if(ctx->cache.dirty) BS_DrawAll(ctx,false,RECT{}); }
    __declspec(dllexport) void BS_SetLogger(BS_NativeContext* ctx, BS_LogFn fn){ if(ctx) ctx->logger=fn; }
    __declspec(dllexport) void BS_SetPartialRatio(BS_NativeContext* ctx, float ratio){ if(ctx){ if(ratio<0) ratio=0; if(ratio>1) ratio=1; ctx->partialRatio=ratio; } }
    __declspec(dllexport) void BS_EnableMerge(BS_NativeContext* ctx, int enable){ if(ctx) ctx->mergeOverlap = (enable!=0); }
}
#pragma managed(pop)

using namespace BorderServiceCpp;

// Use GCHandle for global logger storage instead of managed static handle
static System::Runtime::InteropServices::GCHandle g_loggerHandle; 
static void __stdcall BS_ManagedLogBridge(int level, const wchar_t* msg){ if(!g_loggerHandle.IsAllocated) return; auto del = (BorderLogHandler^)g_loggerHandle.Target; if(del==nullptr) return; System::String^ s = gcnew System::String(msg?msg:L""); del(level,s); }
extern "C" void BS_SetLogger(BS_NativeContext*, BS_LogFn);
extern "C" void BS_SetPartialRatio(BS_NativeContext*, float);
extern "C" void BS_EnableMerge(BS_NativeContext*, int);

BorderServiceHost::BorderServiceHost(int argbColor,int thickness,bool debug)
    :_nativeCtx(System::IntPtr::Zero),_disposed(false),_debug(debug),_thickness(thickness),_colorARGB(argbColor),_logger(nullptr){ _initThreadId=System::Environment::CurrentManagedThreadId; auto ctx=BS_CreateContext(argbColor,thickness,debug?1:0); _nativeCtx=System::IntPtr(ctx); }
BorderServiceHost::~BorderServiceHost(){ Destroy(); }
BorderServiceHost::!BorderServiceHost(){ Destroy(); }
void BorderServiceHost::Destroy(){ if(_disposed) return; _disposed=true; if(_nativeCtx!=System::IntPtr::Zero){ BS_DestroyContext((BS_NativeContext*)_nativeCtx.ToPointer()); _nativeCtx=System::IntPtr::Zero; } if(_logHandle.IsAllocated) _logHandle.Free(); if(g_loggerHandle.IsAllocated) g_loggerHandle.Free(); }
void BorderServiceHost::Update(array<ManagedRect>^ normalRects, array<ManagedRect>^ topRects){ if(_disposed) return; auto ctxPtr=(BS_NativeContext*)_nativeCtx.ToPointer(); if(!ctxPtr) return; pin_ptr<ManagedRect> pNormal = (normalRects && normalRects->Length>0)? &normalRects[0] : nullptr; pin_ptr<ManagedRect> pTop = (topRects && topRects->Length>0)? &topRects[0] : nullptr; BS_UpdateRects(ctxPtr, (BS_NativeRect*)pNormal, normalRects? normalRects->Length:0, (BS_NativeRect*)pTop, topRects? topRects->Length:0); }
void BorderServiceHost::UpdateColor(int argbColor){ _colorARGB=argbColor; if(_nativeCtx!=System::IntPtr::Zero) BS_UpdateColor((BS_NativeContext*)_nativeCtx.ToPointer(), argbColor); }
void BorderServiceHost::UpdateThickness(int t){ _thickness=t; if(_nativeCtx!=System::IntPtr::Zero) BS_UpdateThickness((BS_NativeContext*)_nativeCtx.ToPointer(), t); }
void BorderServiceHost::ForceRedraw(){ if(_nativeCtx!=System::IntPtr::Zero) BS_ForceRedraw((BS_NativeContext*)_nativeCtx.ToPointer()); }
void BorderServiceHost::RepaintCached(){ ForceRedraw(); }
void BorderServiceHost::SetLogger(BorderLogHandler^ handler){ _logger=handler; if(_nativeCtx==System::IntPtr::Zero) return; if(handler==nullptr){ BS_SetLogger((BS_NativeContext*)_nativeCtx.ToPointer(), nullptr); if(g_loggerHandle.IsAllocated) g_loggerHandle.Free(); return; } if(g_loggerHandle.IsAllocated) g_loggerHandle.Free(); g_loggerHandle=System::Runtime::InteropServices::GCHandle::Alloc(handler); BS_SetLogger((BS_NativeContext*)_nativeCtx.ToPointer(), &BS_ManagedLogBridge); }
void BorderServiceHost::SetPartialRedrawRatio(float ratio01){ if(_nativeCtx!=System::IntPtr::Zero) BS_SetPartialRatio((BS_NativeContext*)_nativeCtx.ToPointer(), ratio01); }
void BorderServiceHost::EnableOverlapMerge(bool enable){ if(_nativeCtx!=System::IntPtr::Zero) BS_EnableMerge((BS_NativeContext*)_nativeCtx.ToPointer(), enable?1:0); }

