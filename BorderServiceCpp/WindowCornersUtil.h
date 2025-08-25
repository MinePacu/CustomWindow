#pragma once

#include <Windows.h>

namespace WindowCornerUtils
{
    inline float CornersRadius(HWND window)
    {
        // Simple placeholder - return default corner radius
        // In a real implementation, this would query the window's corner preferences
        return 8.0f;  // Default 8px corner radius
    }
}