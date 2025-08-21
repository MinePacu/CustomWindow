using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing;
using CustomWindow.Utility;
using Xunit;

namespace CustomWindow.Tests;

// STA 스레드 컬렉션 정의
[CollectionDefinition("STA" , DisableParallelization = true)]
public class StaCollection : ICollectionFixture<StaFixture> { }

public class StaFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly AutoResetEvent _ready = new(false);
    public StaFixture()
    {
        _thread = new Thread(() => { System.Windows.Forms.Application.OleRequired(); _ready.Set(); System.Windows.Forms.Application.Run(); })
        { IsBackground = true, Name = "STA-Test-MessageLoop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.WaitOne();
    }
    public void Dispose()
    {
        try { System.Windows.Forms.Application.ExitThread(); } catch { }
    }
}

[Collection("STA")]
public class WindowHighlighterTests
{
    // 실행할 테스트 대상 프로세스 이름 (확장자 없이). 예: "notepad" 혹은 사용자가 실행 상태로 둔 타 프로그램 이름.
    // 테스트 실행 전, 해당 프로세스를 직접 실행하고 메인 창이 떠 있는지 확인하세요.
    private const string TargetProcessNameEnv = "WINDOW_HIGHLIGHTER_TEST_PROCESS"; // 환경 변수 키

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    [Fact(DisplayName = "윈도우 하이라이터: 대상 창을 추적하여 오버레이 생성/갱신")]
    public void Highlight_AttachAndFollowWindow()
    {
        string? procName = Environment.GetEnvironmentVariable(TargetProcessNameEnv);
        Assert.False(string.IsNullOrWhiteSpace(procName),
            $"환경 변수 {TargetProcessNameEnv}를 대상 프로세스 이름(예: notepad)으로 설정하고 테스트를 재실행하십시오.");

        var processes = Process.GetProcessesByName(procName!);
        Assert.True(processes.Length > 0, $"프로세스 '{procName}' 가 실행 중이 아닙니다.");

        // 첫 번째 프로세스 사용
        var p = processes[0];
        IntPtr hwnd = p.MainWindowHandle;

        // 메인 윈도우 핸들이 없으면 UI 나타날 때까지 잠시 대기
        if (hwnd == IntPtr.Zero)
        {
            for (int i = 0; i < 10 && hwnd == IntPtr.Zero; i++)
            {
                Thread.Sleep(300);
                p.Refresh();
                hwnd = p.MainWindowHandle;
            }
        }
        Assert.NotEqual(IntPtr.Zero, hwnd);
        Assert.True(IsWindow(hwnd) && IsWindowVisible(hwnd), "대상 창이 유효/표시 상태가 아닙니다.");

        using var highlighter = new WindowHighlighter(hwnd, Color.Lime, thickness: 3);

        // 색상 변경 테스트
        highlighter.UpdateColor(Color.Red);
        highlighter.UpdateThickness(5);

        // 사용자가 실제로 창 이동을 눈으로 확인할 수 있도록 약간의 대기.
        // (자동화 검증이 어려운 시각적 요소이므로 존재 여부까지만 검증)
        Thread.Sleep(800); // 시각적 확인 유예

        // 프로세스가 종료되기 전까지 예외 없이 유지되면 성공으로 간주.
        Assert.False(p.HasExited, "테스트 중 대상 프로세스가 종료되었습니다.");
    }
}

/*
실행 방법 예 (PowerShell):

$env:WINDOW_HIGHLIGHTER_TEST_PROCESS = "notepad"; dotnet test .\CustomWindow\CustomWindow.Tests\

미리 notepad.exe 를 실행해 두십시오. 다른 프로그램을 쓰고 싶다면 환경 변수 값을 해당 프로세스 이름으로 변경.
*/
