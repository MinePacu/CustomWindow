#pragma once
#include "pch.h"
#include "Globals.h"

LRESULT CALLBACK OverlayProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);
HWND CreateOverlayWindow(bool visible);
void InitTrayIcon(HWND hwnd);
void InstallWinEventHooks();
void UninstallWinEventHooks();
void CALLBACK WinEventProc(HWINEVENTHOOK, DWORD eventId, HWND hwnd, LONG idObject, LONG, DWORD, DWORD);
