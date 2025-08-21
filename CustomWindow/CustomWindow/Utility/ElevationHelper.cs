using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace CustomWindow.Utility;

public static class ElevationHelper
{
    /// <summary>
    /// 현재 프로세스가 관리자 권한(승격)으로 실행 중인지 여부.
    /// </summary>
    public static bool IsRunAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 관리자 권한으로 현재 프로세스를 다시 시작. 성공 시 현재 프로세스는 종료된다.
    /// 실패 시 예외를 throw 한다.
    /// </summary>
    public static void RestartAsAdmin(string? extraArgs = null)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("실행 파일 경로를 찾을 수 없습니다.");

        if (!File.Exists(exePath))
            throw new FileNotFoundException("실행 파일이 존재하지 않습니다.", exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = extraArgs ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };

        Process? started = Process.Start(psi); // UAC 취소 시 Win32Exception(1223) 발생
        if (started is null)
            throw new InvalidOperationException("관리자 권한 프로세스 시작에 실패했습니다.");

        // 새 프로세스 시작이 확인된 후 종료
        Environment.Exit(0);
    }

    /// <summary>
    /// 관리자 재시작을 시도한다. 사용자가 UAC를 취소하면 false 반환 (cancelled=true).
    /// 기타 오류(예: 패키지 앱 상승 불가, 실행 파일 찾기 실패 등)는 false 반환 (cancelled=false) 하며 예외를 throw 한다.
    /// 성공 시 현재 프로세스는 종료되어 반환되지 않는다.
    /// </summary>
    public static bool TryRestartAsAdmin(out bool cancelled, string? extraArgs = null)
    {
        cancelled = false;
        try
        {
            RestartAsAdmin(extraArgs);
            return true; // 도달 불가
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (사용자 취소)
        {
            cancelled = true;
            Debug.WriteLine($"[ElevationHelper] UAC 취소됨: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            // 일반 오류: 패키지 앱의 상승 불가, 권한 문제 등
            Debug.WriteLine($"[ElevationHelper] 관리자 재시작 실패: {ex}");
            return false;
        }
    }
}
