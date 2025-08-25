#include "pch.h"
#include "VirtualDesktopUtils.h"
#include <wil/registry.h>

// Non-Localizable strings
namespace NonLocalizable
{
    const wchar_t RegKeyVirtualDesktops[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops";
}

HKEY OpenVirtualDesktopsRegKey()
{
    HKEY hKey{ nullptr };
    if (RegOpenKeyEx(HKEY_CURRENT_USER, NonLocalizable::RegKeyVirtualDesktops, 0, KEY_ALL_ACCESS, &hKey) == ERROR_SUCCESS)
    {
        return hKey;
    }
    return nullptr;
}

HKEY GetVirtualDesktopsRegKey()
{
    static wil::unique_hkey virtualDesktopsKey{ OpenVirtualDesktopsRegKey() };
    return virtualDesktopsKey.get();
}

VirtualDesktopUtils::VirtualDesktopUtils()
{
    auto res = CoCreateInstance(CLSID_VirtualDesktopManager, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&m_vdManager));
    if (FAILED(res))
    {
        m_vdManager = nullptr;
        // Log error if needed
    }
}

VirtualDesktopUtils::~VirtualDesktopUtils()
{
    if (m_vdManager)
    {
        m_vdManager->Release();
        m_vdManager = nullptr;
    }
}

bool VirtualDesktopUtils::IsWindowOnCurrentDesktop(HWND window) const
{
    std::optional<GUID> id = GetDesktopId(window);
    return id.has_value();
}

std::optional<GUID> VirtualDesktopUtils::GetDesktopId(HWND window) const
{
    if (!m_vdManager || !window)
    {
        return std::nullopt;
    }
    
    GUID id;
    BOOL isWindowOnCurrentDesktop = false;
    
    HRESULT hr = m_vdManager->IsWindowOnCurrentVirtualDesktop(window, &isWindowOnCurrentDesktop);
    if (SUCCEEDED(hr) && isWindowOnCurrentDesktop)
    {
        // Filter windows such as Windows Start Menu, Task Switcher, etc.
        hr = m_vdManager->GetWindowDesktopId(window, &id);
        if (SUCCEEDED(hr) && id != GUID_NULL)
        {
            return id;
        }
    }

    return std::nullopt;
}