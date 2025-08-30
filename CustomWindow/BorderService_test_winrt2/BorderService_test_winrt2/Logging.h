#pragma once
#include "pch.h"
#include <string>

inline void DebugLog(const std::wstring& s)
{
    OutputDebugStringW((s + L"\n").c_str());
    if (GetConsoleWindow()) {
        _putws(s.c_str());
    }
}

inline void EnsureConsole(bool enable)
{
    if (!enable) return;
    if (GetConsoleWindow()) return;
    if (AllocConsole())
    {
        FILE* fp;
        freopen_s(&fp, "CONOUT$", "w", stdout);
        freopen_s(&fp, "CONOUT$", "w", stderr);
        freopen_s(&fp, "CONIN$",  "r", stdin);
        DebugLog(L"[Overlay] Console allocated");
    }
}
