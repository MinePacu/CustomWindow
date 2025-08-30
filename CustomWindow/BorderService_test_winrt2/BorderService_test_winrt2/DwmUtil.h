#pragma once
#include "pch.h"
#include "Globals.h"
#include "Logging.h"
#include <vector>

bool IsAltTabEligible(HWND h);
bool GetWindowBounds(HWND h, RECT& out);
std::vector<HWND> CollectUserVisibleWindows();
void ApplyDwmAttributesToTargets(const std::vector<HWND>& targets);
void ApplyDwmToAllCurrent();
COLORREF ToCOLORREF(const D2D1_COLOR_F& c);
