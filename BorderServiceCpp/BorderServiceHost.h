#pragma once
#include <windows.h>
#include <atomic>
#include <string>

// 단순 네이티브 BorderServiceHost (렌더링/DirectComposition 실제 구현은 후속 작업용 스텁)
// C# P/Invoke 에서 요구하는 상태/설정 저장 및 업데이트 함수만 제공.

struct BS_Context;

// 로거 함수 (StdCall, wide string)
typedef void(__stdcall* BS_LogFn)(int level, const wchar_t* message);

struct BS_Context {
    int argbColor;
    int thickness;
    int debug;
    float partialRatio;
    bool mergeEnabled;
    BS_LogFn logger;

    // 확장용 placeholder
    BS_Context(int color, int t, int dbg)
        : argbColor(color), thickness(t), debug(dbg), partialRatio(0.0f), mergeEnabled(false), logger(nullptr) {}

    void Log(int level, const wchar_t* msg) {
        if (logger) logger(level, msg);
    }
};

// Exported C API (cdecl)
extern "C" {
    __declspec(dllexport) BS_Context* BS_CreateContext(int argb, int thickness, int debug);
    __declspec(dllexport) void BS_DestroyContext(BS_Context* ctx);
    __declspec(dllexport) void BS_UpdateColor(BS_Context* ctx, int argb);
    __declspec(dllexport) void BS_UpdateThickness(BS_Context* ctx, int t);

    struct BS_NativeRect { int Left, Top, Right, Bottom; };
    __declspec(dllexport) void BS_UpdateRects(BS_Context* ctx, const BS_NativeRect* normal, int normalCount, const BS_NativeRect* top, int topCount);
    __declspec(dllexport) void BS_ForceRedraw(BS_Context* ctx);
    __declspec(dllexport) void BS_SetLogger(BS_Context* ctx, BS_LogFn logger);
    __declspec(dllexport) void BS_SetPartialRatio(BS_Context* ctx, float ratio01);
    __declspec(dllexport) void BS_EnableMerge(BS_Context* ctx, int enable);
    // New: update target window list (layered overlay creation)
    __declspec(dllexport) void BS_UpdateWindows(BS_Context* ctx, const HWND* hwnds, int count);
}

// DLL 내 자동 실행 스레드 제어용 헬퍼
void BS_Internal_StartDefaultIfNeeded();
void BS_Internal_StopIfNeeded();