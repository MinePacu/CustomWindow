// dllmain.cpp : DLL 애플리케이션의 진입점을 정의합니다.
#include "pch.h"
#include "BorderServiceHost.h"

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule); // 스레드 attach/detach 콜백 불필요
        BS_Internal_StartDefaultIfNeeded();
        break;
    case DLL_PROCESS_DETACH:
        BS_Internal_StopIfNeeded();
        break;
    }
    return TRUE;
}

