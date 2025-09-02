#include "pch.h"
#include "Globals.h"

RenderMode g_mode = RenderMode::Auto;
bool g_console = false;
D2D1_COLOR_F g_borderColor = D2D1::ColorF(0.0f, 0.8f, 1.0f, 1.0f);
float g_thickness = 3.0f;
bool g_foregroundWindowOnly = false; // 새로 추가: 포그라운드 창 전용 모드

HWND g_overlay = nullptr;
RECT g_virtualScreen{};

HWINEVENTHOOK g_hook1 = nullptr, g_hook2 = nullptr, g_hook3 = nullptr;
HWINEVENTHOOK g_hook4 = nullptr, g_hook5 = nullptr, g_hook6 = nullptr;

std::unordered_map<HWND, RECT, HwndHash, HwndEq> g_targets;
std::unordered_map<HWND, AppliedState, HwndHash, HwndEq> g_applied;

Microsoft::WRL::ComPtr<ID2D1Factory1> g_d2dFactory;
Microsoft::WRL::ComPtr<ID2D1Device> g_d2dDevice;
Microsoft::WRL::ComPtr<ID2D1DeviceContext> g_d2dCtx;
Microsoft::WRL::ComPtr<ID3D11Device> g_d3d;
Microsoft::WRL::ComPtr<ID3D11DeviceContext> g_d3dCtx;
Microsoft::WRL::ComPtr<IDXGIDevice> g_dxgiDevice;
Microsoft::WRL::ComPtr<IDCompositionDevice> g_dcompDevice;
Microsoft::WRL::ComPtr<IDCompositionTarget> g_dcompTarget;
Microsoft::WRL::ComPtr<IDCompositionVisual> g_rootVisual;
Microsoft::WRL::ComPtr<IDCompositionVisual> g_surfaceVisual;
Microsoft::WRL::ComPtr<IDCompositionSurface> g_surface;
UINT g_surfaceW = 0, g_surfaceH = 0;

NOTIFYICONDATAW g_nid = { 0 };
HICON g_trayIcon = nullptr;
