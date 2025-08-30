#pragma once
#include "pch.h"
#include "Globals.h"
#include <vector>

HRESULT CreateD3DDevice();
HRESULT CreateD2D();
HRESULT CreateDComp(HWND hwnd);
HRESULT EnsureSurface(UINT width, UINT height);
void BeginDrawOnSurface(UINT width, UINT height, ID2D1DeviceContext** outCtx, POINT* offset);
void EndDrawOnSurface();
void UpdateVirtualScreenAndResize();
void DrawBorders(ID2D1DeviceContext* ctx, const std::vector<RECT>& rects);
void UpdateOverlayRegion(const std::vector<RECT>& zorderedRects);
void RefreshOverlay();
