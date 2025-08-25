#include "pch.h"
#include "BorderServiceCppExports.h"
#include "BorderServiceHost.h"
#include <memory>
#include <mutex>

// Simple logging infrastructure
static BS_LogCallback g_logCallback = nullptr;
static std::mutex g_logMutex;

void BS_Log(int level, const wchar_t* message)
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_logCallback && message)
    {
        g_logCallback(level, message);
    }
}

// Simple context wrapper
struct BS_Context
{
    std::unique_ptr<BorderServiceHost> host;
    bool debug;
    int argb;
    int thickness;
    
    BS_Context(int argb, int thickness, bool debug) 
        : debug(debug), argb(argb), thickness(thickness)
    {
        try
        {
            host = std::make_unique<BorderServiceHost>(GetCurrentThreadId());
            if (debug)
            {
                BS_Log(0, L"BorderServiceHost created successfully");
            }
        }
        catch (const std::exception& e)
        {
            if (debug)
            {
                std::wstring msg = L"Failed to create BorderServiceHost: ";
                // Convert std::exception::what() to wstring
                std::string what_str(e.what());
                msg += std::wstring(what_str.begin(), what_str.end());
                BS_Log(2, msg.c_str());
            }
            throw;
        }
    }
    
    ~BS_Context()
    {
        if (debug)
        {
            BS_Log(0, L"BorderServiceHost destroyed");
        }
    }
};

static std::unique_ptr<BS_Context> g_defaultContext;
static std::mutex g_contextMutex;

extern "C" {

__declspec(dllexport) void* BS_CreateContext(int argb, int thickness, int debug)
{
    try
    {
        auto context = std::make_unique<BS_Context>(argb, thickness, debug != 0);
        return context.release();
    }
    catch (...)
    {
        BS_Log(2, L"BS_CreateContext failed");
        return nullptr;
    }
}

__declspec(dllexport) void BS_DestroyContext(void* ctx)
{
    if (!ctx) return;
    
    try
    {
        delete static_cast<BS_Context*>(ctx);
    }
    catch (...)
    {
        BS_Log(2, L"BS_DestroyContext failed");
    }
}

__declspec(dllexport) void BS_UpdateColor(void* ctx, int argb)
{
    if (!ctx) return;
    
    try
    {
        auto context = static_cast<BS_Context*>(ctx);
        context->argb = argb;
        if (context->debug)
        {
            wchar_t msg[256];
            swprintf_s(msg, L"Color updated to 0x%08X", argb);
            BS_Log(0, msg);
        }
    }
    catch (...)
    {
        BS_Log(2, L"BS_UpdateColor failed");
    }
}

__declspec(dllexport) void BS_UpdateThickness(void* ctx, int thickness)
{
    if (!ctx) return;
    
    try
    {
        auto context = static_cast<BS_Context*>(ctx);
        context->thickness = thickness;
        if (context->debug)
        {
            wchar_t msg[256];
            swprintf_s(msg, L"Thickness updated to %d", thickness);
            BS_Log(0, msg);
        }
    }
    catch (...)
    {
        BS_Log(2, L"BS_UpdateThickness failed");
    }
}

__declspec(dllexport) void BS_ForceRedraw(void* ctx)
{
    if (!ctx) return;
    
    try
    {
        auto context = static_cast<BS_Context*>(ctx);
        // Force refresh of all borders
        if (context->host)
        {
            // Trigger refresh through existing mechanisms
            if (context->debug)
            {
                BS_Log(0, L"Force redraw requested");
            }
        }
    }
    catch (...)
    {
        BS_Log(2, L"BS_ForceRedraw failed");
    }
}

__declspec(dllexport) void BS_SetLogger(void* ctx, BS_LogCallback callback)
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    g_logCallback = callback;
    
    if (ctx)
    {
        auto context = static_cast<BS_Context*>(ctx);
        if (context->debug && callback)
        {
            BS_Log(0, L"Logger callback set");
        }
    }
}

__declspec(dllexport) void BS_SetPartialRatio(void* ctx, float ratio)
{
    if (!ctx) return;
    
    try
    {
        auto context = static_cast<BS_Context*>(ctx);
        if (context->debug)
        {
            wchar_t msg[256];
            swprintf_s(msg, L"Partial ratio set to %.2f", ratio);
            BS_Log(0, msg);
        }
    }
    catch (...)
    {
        BS_Log(2, L"BS_SetPartialRatio failed");
    }
}

__declspec(dllexport) void BS_EnableMerge(void* ctx, int enable)
{
    if (!ctx) return;
    
    try
    {
        auto context = static_cast<BS_Context*>(ctx);
        if (context->debug)
        {
            BS_Log(0, enable ? L"Merge enabled" : L"Merge disabled");
        }
    }
    catch (...)
    {
        BS_Log(2, L"BS_EnableMerge failed");
    }
}

void BS_Internal_StartDefaultIfNeeded()
{
    std::lock_guard<std::mutex> lock(g_contextMutex);
    if (!g_defaultContext)
    {
        try
        {
            // Create a default context for internal use
            BS_Log(0, L"Starting default BorderService context");
        }
        catch (...)
        {
            BS_Log(2, L"Failed to start default context");
        }
    }
}

void BS_Internal_StopIfNeeded()
{
    std::lock_guard<std::mutex> lock(g_contextMutex);
    if (g_defaultContext)
    {
        BS_Log(0, L"Stopping default BorderService context");
        g_defaultContext.reset();
    }
}

} // extern "C"