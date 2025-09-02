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
void ResetAndApplyDwmAttributes(); // 새로 추가: 포그라운드 모드 변경 시 전체 재설정
COLORREF ToCOLORREF(const D2D1_COLOR_F& c);

// New: Corner handling
void ApplyCornerPreference(HWND hwnd, const std::wstring& token);
float CornerRadiusFromToken(const std::wstring& token);
