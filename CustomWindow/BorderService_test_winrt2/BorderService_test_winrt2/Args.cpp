#include "pch.h"
#include "Globals.h"
#include "Logging.h"
#include "ConsoleUtil.h"
#include "Args.h"

static bool ParseColorString(const wchar_t* hex, D2D1_COLOR_F& out);

std::wstring g_cornerToken = L"default";

bool IsWindows11OrGreater()
{
    typedef LONG (WINAPI* RtlGetVersionPtr)(PRTL_OSVERSIONINFOW);
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!ntdll) return false;
    auto fn = reinterpret_cast<RtlGetVersionPtr>(GetProcAddress(ntdll, "RtlGetVersion"));
    if (!fn) return false;
    RTL_OSVERSIONINFOW v{}; v.dwOSVersionInfoSize = sizeof(v);
    if (fn(&v) != 0) return false;
    return (v.dwMajorVersion > 10) || (v.dwMajorVersion == 10 && v.dwBuildNumber >= 22000);
}

void ParseArgsAndApply()
{
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return;

    auto tolower = [](std::wstring s) {
        std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c){ return (wchar_t)::towlower(c); });
        return s;
    };

    for (int i = 0; i < argc; ++i)
    {
        std::wstring arg = tolower(argv[i]);
        if (arg == L"--console") { g_console = true; continue; }

        if (arg == L"--foregroundonly" && i + 1 < argc) {
            std::wstring v = tolower(argv[i + 1]);
            g_foregroundWindowOnly = (v == L"1" || v == L"true" || v == L"on");
            ++i; continue;
        }
        if (arg.rfind(L"--foregroundonly=", 0) == 0) {
            std::wstring v = tolower(arg.substr(17));
            g_foregroundWindowOnly = (v == L"1" || v == L"true" || v == L"on");
            continue;
        }

        if (arg == L"--mode" && i + 1 < argc) {
            std::wstring v = tolower(argv[i + 1]);
            if (v == L"dwm") g_mode = RenderMode::Dwm;
            else if (v == L"dcomp") g_mode = RenderMode::DComp;
            else g_mode = RenderMode::Auto;
            ++i; continue;
        }
        if (arg.rfind(L"--mode", 0) == 0 && arg.size() > 7) {
            std::wstring v = tolower(arg.substr(7));
            if (v == L"dwm") g_mode = RenderMode::Dwm;
            else if (v == L"dcomp") g_mode = RenderMode::DComp;
            else g_mode = RenderMode::Auto;
            continue;
        }

        if (arg == L"--corner" && i + 1 < argc) {
            g_cornerToken = tolower(argv[++i]);
            continue;
        }
        if (arg.rfind(L"--corner=", 0) == 0) {
            g_cornerToken = tolower(arg.substr(9));
            continue;
        }

        if (arg == L"--color" && i + 1 < argc) {
            D2D1_COLOR_F cf{};
            if (ParseColorString(argv[i + 1], cf)) { g_borderColor = cf; DebugLog(L"[Overlay] Arg color"); }
            ++i; continue;
        }
        if (arg.rfind(L"--color=", 0) == 0) {
            std::wstring val = std::wstring(argv[i] + 8);
            D2D1_COLOR_F cf{};
            if (ParseColorString(val.c_str(), cf)) { g_borderColor = cf; DebugLog(L"[Overlay] Arg color"); }
            continue;
        }
        if (arg == L"--thickness" && i + 1 < argc) {
            try { float tv = std::stof(argv[i + 1]); if (tv > 0 && tv < 1000) { g_thickness = tv; DebugLog(L"[Overlay] Arg thickness"); } } catch (...) {}
            ++i; continue;
        }
        if (arg.rfind(L"--thickness=", 0) == 0) {
            std::wstring val = std::wstring(argv[i] + 12);
            try { float tv = std::stof(val); if (tv > 0 && tv < 1000) { g_thickness = tv; DebugLog(L"[Overlay] Arg thickness"); } } catch (...) {}
            continue;
        }
    }

    LocalFree(argv);

    if (g_console) {
        EnsureConsole(true);
        ConfigureConsoleWindow();
        ShowConsole(true);
    }

    if (g_mode == RenderMode::Auto) {
        g_mode = IsWindows11OrGreater() ? RenderMode::Dwm : RenderMode::DComp;
    }
    DebugLog(L"[Overlay] Mode decided");
}

static bool ParseColorString(const wchar_t* hex, D2D1_COLOR_F& out)
{
    if (!hex || !hex[0]) return false;
    std::wstring h = hex;
    if (h[0] == L'#') h.erase(h.begin());
    if (h.size() != 6 && h.size() != 8) return false;
    unsigned int val = 0;
    try { val = std::stoul(h, nullptr, 16); } catch (...) { return false; }

    float a = 1.0f, r = 0, g = 0, b = 0;
    if (h.size() == 8) {
        unsigned int A = (val >> 24) & 0xFF; 
        unsigned int R = (val >> 16) & 0xFF; 
        unsigned int G = (val >> 8) & 0xFF; 
        unsigned int B = (val) & 0xFF;
        a = A / 255.0f; 
        r = R / 255.0f; 
        g = G / 255.0f; 
        b = B / 255.0f;
    } else {
        unsigned int R = (val >> 16) & 0xFF; 
        unsigned int G = (val >> 8) & 0xFF; 
        unsigned int B = (val) & 0xFF;
        r = R / 255.0f; 
        g = G / 255.0f; 
        b = B / 255.0f; 
        a = 1.0f;
    }
    out = D2D1::ColorF(r, g, b, a);
    return true;
}
