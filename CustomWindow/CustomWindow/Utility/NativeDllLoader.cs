using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomWindow.Utility;

/// <summary>
/// Native DLL 로딩을 위한 헬퍼 클래스
/// </summary>
public static class NativeDllLoader
{
    private static IntPtr _borderServiceHandle = IntPtr.Zero;

    /// <summary>
    /// BorderServiceCpp.dll을 수동으로 로드
    /// </summary>
    public static bool LoadBorderServiceDll()
    {
        if (_borderServiceHandle != IntPtr.Zero)
        {
            return true; // 이미 로드됨
        }

        var dllPath = FindBorderServiceDll();
        if (string.IsNullOrEmpty(dllPath))
        {
            return false;
        }

        // 의존성 DLL들을 먼저 로드
        LoadDependencyDlls(Path.GetDirectoryName(dllPath)!);

        _borderServiceHandle = LoadLibrary(dllPath);
        if (_borderServiceHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"Failed to load {dllPath}, error: {error}");
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"Successfully loaded BorderServiceCpp.dll from {dllPath}");
        return true;
    }

    /// <summary>
    /// 로드된 DLL을 해제
    /// </summary>
    public static void UnloadBorderServiceDll()
    {
        if (_borderServiceHandle != IntPtr.Zero)
        {
            FreeLibrary(_borderServiceHandle);
            _borderServiceHandle = IntPtr.Zero;
        }
    }

    private static string? FindBorderServiceDll()
    {
        var searchPaths = new[]
        {
            // 1. 실행 파일과 같은 디렉토리
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            // 2. 현재 작업 디렉토리
            Directory.GetCurrentDirectory(),
            // 3. AppDomain 기본 디렉토리
            AppDomain.CurrentDomain.BaseDirectory,
            // 4. 환경 현재 디렉토리
            Environment.CurrentDirectory,
            // 5. 특정 빌드 경로들
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64", "Debug"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "x64", "Release"),
        };

        foreach (var searchPath in searchPaths)
        {
            if (string.IsNullOrEmpty(searchPath)) continue;

            var dllPath = Path.Combine(searchPath, "BorderServiceCpp.dll");
            if (File.Exists(dllPath))
            {
                System.Diagnostics.Debug.WriteLine($"Found BorderServiceCpp.dll at: {dllPath}");
                return dllPath;
            }
        }

        System.Diagnostics.Debug.WriteLine("BorderServiceCpp.dll not found in any search path");
        return null;
    }

    private static void LoadDependencyDlls(string dllDirectory)
    {
        // Visual C++ Redistributable 런타임 의존성들
        var dependencies = new[]
        {
            "vcruntime140.dll",
            "vcruntime140_1.dll", 
            "msvcp140.dll",
            "msvcp140_1.dll",
            "msvcp140_2.dll"
        };

        foreach (var dep in dependencies)
        {
            var depPath = Path.Combine(dllDirectory, dep);
            if (File.Exists(depPath))
            {
                var handle = LoadLibrary(depPath);
                if (handle != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"Loaded dependency: {dep}");
                }
            }
        }
    }

    #region Windows API
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
    #endregion
}