#pragma once
#include "pch.h"
#include "Globals.h"

void ParseArgsAndApply();
bool IsWindows11OrGreater();

// Exposed parsed corner token: "default|donot|round|roundsmall"
extern std::wstring g_cornerToken;
