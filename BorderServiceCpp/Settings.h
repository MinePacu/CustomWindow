#pragma once

// Placeholder settings structure for BorderServiceCpp
struct AlwaysOnTopSettings
{
    struct SettingsData
    {
        bool frameAccentColor = false;
        COLORREF frameColor = RGB(255, 0, 0);
        float frameOpacity = 0.8f;
        int frameThickness = 2;
        bool roundCornersEnabled = false;
        bool enableFrame = true;
    };
    
    static SettingsData& settings()
    {
        static SettingsData instance;
        return instance;
    }
};

enum class SettingId
{
    FrameThickness,
    FrameColor,
    FrameAccentColor,
    FrameOpacity,
    RoundCornersEnabled
};