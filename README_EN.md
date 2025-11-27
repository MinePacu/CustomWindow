# CustomWindow

[English](README_EN.md) | [한국어](README.md)

**CustomWindow** is a utility that allows users to customize the window borders of Windows applications as they wish. It features an intuitive settings UI based on WinUI 3 and [...]

## ✨ Key Features

*   **Border Customization**: Freely customize color, thickness, corner radius, and more.
*   **Auto Detection (AutoWindowChange)**: Automatically detects the currently active window or visible windows to apply borders.
*   **Various Rendering Modes**: Supports Auto, DWM, and DComp modes to provide optimized rendering for your system environment.
*   **Convenience Features**: Supports minimizing to system tray and auto-start on Windows startup.

## 🛠️ Tech Stack

This project consists of two main components:

*   **GUI (CustomWindow)**: A settings management interface built using WinUI 3 (Windows App SDK) and .NET 8.
*   **Service (BorderService_test_winrt2)**: A high-performance overlay service built using C++20, Direct2D, DirectComposition, and DWM API.

## 📋 Requirements

*   **OS**: Windows 10 20H1 (Build 19041) or higher
*   **Dev Tools**: Visual Studio 2022
*   **Required Packages**:
    *   Microsoft.WindowsAppSDK 1.7 or higher
    *   Microsoft.Windows.SDK.BuildTools

## 🚀 Build & Installation

1.  Open the solution file (`CustomWindow.sln`) in Visual Studio 2022.
2.  Set the solution configuration to **x64**.
3.  Restore NuGet packages.
4.  Build the entire solution.

## 📖 Usage

1.  Run the **CustomWindow** app.
2.  Set your desired border style in the **Normal** tab.
3.  Once set, the service will automatically draw the borders.
4.  Closing the app will minimize it to the system tray, running in the background.

### Service CLI Options

For advanced users, the background service (`BorderService`) supports command-line arguments.

*   `--console`: Displays a console window for debugging.
*   `--mode {auto|dwm|dcomp}`: Forces a specific rendering mode.
*   `--color #RRGGBB` or `#AARRGGBB`: Specifies the border color.
*   `--thickness N`: Specifies the border thickness in `float`.

## 📂 Project Structure

*   `CustomWindow/CustomWindow`: C# WinUI 3 project (UI, tray, service control logic)
*   `CustomWindow/BorderService_test_winrt2`: C++20 project (overlay rendering engine)

## ⚠️ Notes

*   **Minimize**: If the `MinimizeToTray` option is enabled, clicking the close (X) button will minimize the program to the system tray instead of terminating it.
*   **Rendering Compatibility**: If the DWM method is not supported in certain environments like Windows 11, it may automatically switch to the DComp method.
*   **Auto Start**: When the auto-start option is enabled, it registers to the registry path `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
