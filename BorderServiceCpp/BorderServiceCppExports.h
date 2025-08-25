#pragma once

#ifdef __cplusplus
extern "C" {
#endif

// C-style exports for .NET interop
typedef void(__stdcall* BS_LogCallback)(int level, const wchar_t* message);

__declspec(dllexport) void* BS_CreateContext(int argb, int thickness, int debug);
__declspec(dllexport) void BS_DestroyContext(void* ctx);
__declspec(dllexport) void BS_UpdateColor(void* ctx, int argb);
__declspec(dllexport) void BS_UpdateThickness(void* ctx, int thickness);
__declspec(dllexport) void BS_ForceRedraw(void* ctx);
__declspec(dllexport) void BS_SetLogger(void* ctx, BS_LogCallback callback);
__declspec(dllexport) void BS_SetPartialRatio(void* ctx, float ratio);
__declspec(dllexport) void BS_EnableMerge(void* ctx, int enable);

// Internal functions for DLL lifecycle management
void BS_Internal_StartDefaultIfNeeded();
void BS_Internal_StopIfNeeded();

#ifdef __cplusplus
}
#endif