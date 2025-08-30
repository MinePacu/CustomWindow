#pragma once
#include "pch.h"
#include <unordered_map>
#include <unordered_set>

// Render mode
enum class RenderMode { Auto, Dwm, DComp };

// Hash for HWND keys
struct HwndHash {
    size_t operator()(HWND h) const noexcept {
        return std::hash<std::uintptr_t>{}(reinterpret_cast<std::uintptr_t>(h));
    }
};
struct HwndEq { bool operator()(HWND a, HWND b) const noexcept { return a == b; } };

// Globals
extern RenderMode g_mode;
extern bool g_console;
extern D2D1_COLOR_F g_borderColor;
extern float g_thickness;

extern HWND g_overlay;
extern RECT g_virtualScreen;

extern HWINEVENTHOOK g_hook1, g_hook2, g_hook3, g_hook4, g_hook5, g_hook6;

extern std::unordered_map<HWND, RECT, HwndHash, HwndEq> g_targets;
struct AppliedState { COLORREF color; int thickness; };
extern std::unordered_map<HWND, AppliedState, HwndHash, HwndEq> g_applied;

extern Microsoft::WRL::ComPtr<ID2D1Factory1> g_d2dFactory;
extern Microsoft::WRL::ComPtr<ID2D1Device> g_d2dDevice;
extern Microsoft::WRL::ComPtr<ID2D1DeviceContext> g_d2dCtx;
extern Microsoft::WRL::ComPtr<ID3D11Device> g_d3d;
extern Microsoft::WRL::ComPtr<ID3D11DeviceContext> g_d3dCtx;
extern Microsoft::WRL::ComPtr<IDXGIDevice> g_dxgiDevice;
extern Microsoft::WRL::ComPtr<IDCompositionDevice> g_dcompDevice;
extern Microsoft::WRL::ComPtr<IDCompositionTarget> g_dcompTarget;
extern Microsoft::WRL::ComPtr<IDCompositionVisual> g_rootVisual;
extern Microsoft::WRL::ComPtr<IDCompositionVisual> g_surfaceVisual;
extern Microsoft::WRL::ComPtr<IDCompositionSurface> g_surface;
extern UINT g_surfaceW, g_surfaceH;

extern NOTIFYICONDATAW g_nid;
extern HICON g_trayIcon;

// Custom messages
static constexpr UINT WM_APP_REFRESH = WM_APP + 1;
static constexpr UINT WM_APP_TRAY = WM_APP + 2;
