#pragma once

#include "shellapi.h"
// SHQueryUserNotificationState와 S_OK를 사용하려면 아래 헤더를 추가해야 합니다.
#include "shlobj.h"
#include "winerror.h"

inline bool detect_game_mode()
{
    QUERY_USER_NOTIFICATION_STATE notification_state;
    if (SHQueryUserNotificationState(&notification_state) != S_OK)
    {
        return false;
    }
    return (notification_state == QUNS_RUNNING_D3D_FULL_SCREEN);
}