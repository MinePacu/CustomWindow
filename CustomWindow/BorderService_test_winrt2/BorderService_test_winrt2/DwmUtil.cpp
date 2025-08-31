#include "pch.h"
#include "Globals.h"
#include "Logging.h"
#include "DwmUtil.h"
#include "Args.h"

static bool IsWindowCloaked(HWND h)
{
    BOOL cloaked = FALSE;
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))))
        return cloaked != FALSE;
    return false;
}

bool IsAltTabEligible(HWND h)
{
    if (!IsWindowVisible(h) || IsIconic(h)) return false;
    if (GetAncestor(h, GA_ROOT) != h) return false;

    LONG_PTR ex = GetWindowLongPtr(h, GWL_EXSTYLE);
    if (ex & WS_EX_TOOLWINDOW) return false;

    wchar_t cls[128] = {};
    GetClassNameW(h, cls, 128);
    if (wcscmp(cls, L"Shell_TrayWnd") == 0) return false;
    if (wcscmp(cls, L"Progman") == 0) return false;
    if (wcscmp(cls, L"WorkerW") == 0) return false;

    if (IsWindowCloaked(h)) return false;

    return true;
}

bool GetWindowBounds(HWND h, RECT& out)
{
    if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, &out, sizeof(out))))
        return true;
    return GetWindowRect(h, &out) != 0;
}

std::vector<HWND> CollectUserVisibleWindows()
{
    std::vector<HWND> result;
    EnumWindows([](HWND h, LPARAM lParam) -> BOOL {
        auto& vec = *reinterpret_cast<std::vector<HWND>*>(lParam);
        if (!IsAltTabEligible(h)) return TRUE;
        RECT rc{};
        if (!GetWindowBounds(h, rc)) return TRUE;

        RECT inter{};
        if (IntersectRect(&inter, &rc, &g_virtualScreen) && (inter.right > inter.left) && (inter.bottom > inter.top))
            vec.push_back(h);
        return TRUE;
    }, reinterpret_cast<LPARAM>(&result));

    return result;
}

COLORREF ToCOLORREF(const D2D1_COLOR_F& c)
{
    BYTE r = (BYTE)std::clamp((int)std::lround(c.r * 255.0f), 0, 255);
    BYTE g = (BYTE)std::clamp((int)std::lround(c.g * 255.0f), 0, 255);
    BYTE b = (BYTE)std::clamp((int)std::lround(c.b * 255.0f), 0, 255);
    return RGB(r, g, b);
}

void ApplyDwmAttributesToTargets(const std::vector<HWND>& targets)
{
    if (g_mode != RenderMode::Dwm) return;
    COLORREF cr = ToCOLORREF(g_borderColor);
    int thick = (int)g_thickness;
    if (thick < 1) thick = 1; else if (thick > 1000) thick = 1000;

    for (HWND h : targets) {
        if (!IsWindow(h)) continue;
        auto it = g_applied.find(h);
        if (it != g_applied.end() && it->second.color == cr && it->second.thickness == thick) {
            continue; // already applied
        }
        HRESULT hr1 = DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, &cr, sizeof(cr));
        HRESULT hr2 = DwmSetWindowAttribute(h, DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, &thick, sizeof(thick));
        if (SUCCEEDED(hr1) || SUCCEEDED(hr2)) {
            g_applied[h] = { cr, thick };
        }
    }

    for (auto it = g_applied.begin(); it != g_applied.end(); ) {
        if (!IsWindow(it->first)) it = g_applied.erase(it); else ++it;
    }
}

void ApplyDwmToAllCurrent()
{
    if (g_mode != RenderMode::Dwm) return;
    std::vector<HWND> targets;
    targets.reserve(g_applied.size());
    for (const auto& kv : g_applied) {
        if (IsWindow(kv.first)) targets.push_back(kv.first);
    }
    ApplyDwmAttributesToTargets(targets);
}

void ApplyCornerPreference(HWND hwnd, const std::wstring& token)
{
    // Windows 11+: set DWMWA_WINDOW_CORNER_PREFERENCE
    if (IsWindows11OrGreater()) {
        DWORD pref = DWMWCP_DEFAULT;
        if (token == L"donot") pref = DWMWCP_DONOTROUND;
        else if (token == L"round") pref = DWMWCP_ROUND;
        else if (token == L"roundsmall") pref = DWMWCP_ROUNDSMALL;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &pref, sizeof(pref));
    }
}

float CornerRadiusFromToken(const std::wstring& token)
{
    if (token == L"donot") return 0.0f;
    if (token == L"roundsmall") return 6.0f; // small radius
    if (token == L"round") return 12.0f;     // normal radius
    return 8.0f; // default
}
