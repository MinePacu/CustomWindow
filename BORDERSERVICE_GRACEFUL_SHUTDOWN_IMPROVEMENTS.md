# BorderService 정상 종료 개선

## 문제
BorderService_test_winrt2가 트레이에 있는 상태에서 "윈도우 설정 자동 변경" 기능을 끄면, 토글은 껴졌으나 프로세스가 종료되지 않고 트레이에 남아 계속 동작하는 문제가 있었습니다.

## 원인 분석
1. C# 측에서 `BorderService.StopIfRunning()` 호출 시 `Process.Kill()`만 사용
2. C++ 프로그램이 트레이 아이콘을 가지고 메시지 루프를 실행 중
3. `Kill()` 메서드가 트레이 프로세스를 강제 종료하려 했지만, 일부 경우 프로세스가 완전히 종료되지 않음
4. IPC를 통한 정상 종료 메커니즘이 없음

## 해결 방안

### 1. C# 측 개선 (BorderService.cs)

#### A. `StopWinRTConsoleIfRunning()` 메서드 개선
정상 종료 시도 후 강제 종료하는 2단계 접근 방식 구현:

```csharp
private static void StopWinRTConsoleIfRunning()
{
    try
    {
        if (_winrtProc != null && !_winrtProc.HasExited)
        {
            // 1단계: IPC를 통한 정상 종료 시도
            LogMessage("Attempting graceful shutdown via IPC...");
            var hwnd = FindOverlayWindow();
            if (hwnd != IntPtr.Zero)
            {
                if (TrySendCopyData(hwnd, "QUIT"))
                {
                    LogMessage("Sent QUIT command via IPC, waiting for process to exit...");
                    if (_winrtProc.WaitForExit(2000))
                    {
                        LogMessage("Process exited gracefully");
                        _winrtProc = null;
                        _running = false;
                        return;
                    }
                    LogMessage("Process did not exit within timeout, forcing termination...");
                }
                else
                {
                    LogMessage("Failed to send QUIT command, forcing termination...");
                }
            }
            else
            {
                LogMessage("Overlay window not found, forcing termination...");
            }

            // 2단계: 강제 종료 (정상 종료 실패 시)
            _winrtProc.Kill(entireProcessTree: true);
            _winrtProc.WaitForExit(2000);
            LogMessage("Process forcefully terminated");
        }
    }
    catch (Exception ex)
    {
        LogMessage($"Error during process termination: {ex.Message}");
    }
    finally 
    { 
        _winrtProc = null; 
        _running = false; 
    }
}
```

### 2. C++ 측 개선 (Tray.cpp)

#### A. `HandleSettingsMessage()` 함수에 QUIT 명령 처리 추가

```cpp
static void HandleSettingsMessage(const std::wstring& msg)
{
    // Handle QUIT command
    auto msgUpper = msg;
    std::transform(msgUpper.begin(), msgUpper.end(), msgUpper.begin(), ::towupper);
    if (msgUpper == L"QUIT" || msgUpper.rfind(L"QUIT ", 0) == 0) {
        DebugLog(L"[Overlay] Received QUIT command via IPC, posting WM_QUIT");
        PostQuitMessage(0);
        return;
    }

    // ...기존 코드 (SET, REFRESH 처리)...
}
```

## 동작 흐름

### AutoWindowChange OFF 시나리오

1. 사용자가 "윈도우 설정 자동 변경" 토글 OFF
2. `NormalSettingsViewModel.AutoWindowChange` setter 호출
3. `BorderService.StopIfRunning()` 호출
4. `StopWinRTConsoleIfRunning()` 실행:
   
   **정상 종료 시도 (1st Pass)**:
   - 오버레이 윈도우 핸들 찾기
   - `WM_COPYDATA`를 통해 "QUIT" 명령 전송
   - C++ 측에서 `PostQuitMessage(0)` 호출
   - 메시지 루프 종료, 프로세스 정상 종료
   - 2초 대기하여 프로세스 종료 확인
   
   **강제 종료 시도 (2nd Pass - 정상 종료 실패 시)**:
   - 오버레이 윈도우를 찾을 수 없거나
   - IPC 전송이 실패하거나
   - 프로세스가 2초 내에 종료되지 않으면
   - `Process.Kill(entireProcessTree: true)` 호출
   - 추가 2초 대기

5. 창 모서리 복원 (`RestoreWindowCornersToDefault()`)
6. WindowTracker 중지
7. 로그: "BorderService stopped"

## 기술적 세부사항

### IPC 메커니즘
- **메시지 형식**: `"QUIT"` (대소문자 무관)
- **전송 방법**: `WM_COPYDATA` (Win32 메시지)
- **대상**: `BorderOverlayDCompWindowClass` 윈도우
- **타임아웃**: 300ms (SendMessageTimeout)

### 종료 대기 시간
- **정상 종료 대기**: 2000ms (2초)
- **강제 종료 대기**: 2000ms (2초)
- **총 최대 대기 시간**: 4초

### 안전성
- 각 단계에서 예외 처리
- finally 블록에서 확실한 리소스 정리
- 상세한 로깅으로 디버깅 용이

## 로그 메시지

### 정상 종료 성공 시:
```
[HH:mm:ss] [BorderService|EXE] Stopping BorderService
[HH:mm:ss] [BorderService|EXE] Attempting graceful shutdown via IPC...
[HH:mm:ss] [BorderService|EXE] Overlay window found: 0x...
[HH:mm:ss] [BorderService|EXE] IPC -> hwnd=0x..., msg='QUIT'
[HH:mm:ss] [BorderService|EXE] Sent QUIT command via IPC, waiting for process to exit...
[HH:mm:ss] [BorderService|EXE] Process exited gracefully
[HH:mm:ss] [BorderService|IDLE] BorderService stopped
```

### 강제 종료 시:
```
[HH:mm:ss] [BorderService|EXE] Stopping BorderService
[HH:mm:ss] [BorderService|EXE] Attempting graceful shutdown via IPC...
[HH:mm:ss] [BorderService|EXE] Overlay window not found, forcing termination...
[HH:mm:ss] [BorderService|EXE] Process forcefully terminated
[HH:mm:ss] [BorderService|IDLE] BorderService stopped
```

### C++ 측 (DebugLog):
```
[Overlay] WM_COPYDATA received: QUIT
[Overlay] Received QUIT command via IPC, posting WM_QUIT
```

## 추가 개선 사항

### 창 모서리 복원 (이전 커밋)
`AutoWindowChange` OFF 시 모든 창의 모서리를 기본 상태로 자동 복원:
```csharp
// DWM 모드에서 창 모서리를 기본 상태로 복원
RestoreWindowCornersToDefault();
```

## 테스트 시나리오

### 1. 정상 종료 테스트
1. 앱 실행 및 "윈도우 설정 자동 변경" ON
2. 트레이 아이콘 확인
3. "윈도우 설정 자동 변경" OFF
4. 트레이 아이콘이 즉시 사라지는지 확인
5. 작업 관리자에서 프로세스가 종료되었는지 확인

### 2. 강제 종료 테스트
1. C++ 프로세스를 디버거에 연결
2. `PostQuitMessage` 브레이크포인트 설정
3. "윈도우 설정 자동 변경" OFF
4. 브레이크포인트에서 멈춤 방지 (또는 2초 이상 대기)
5. 프로세스가 강제 종료되는지 확인

### 3. 오버레이 없는 상황 테스트
1. BorderService_test_winrt2 수동 종료
2. "윈도우 설정 자동 변경" ON (프로세스 시작 실패)
3. "윈도우 설정 자동 변경" OFF
4. 오류 없이 종료되는지 확인

## 호환성
- **Windows 10**: 완전 지원
- **Windows 11**: 완전 지원
- **.NET 8**: 완전 지원
- **C++17**: 완전 지원

## 성능 영향
- 정상 종료 시: 추가 대기 시간 최대 2초
- 강제 종료 시: 추가 대기 시간 최대 4초
- UI 블로킹: 없음 (작업이 백그라운드 스레드에서 실행)

## 향후 개선 가능성
1. 종료 대기 시간을 설정으로 변경 가능하게
2. 비동기 종료 (await Pattern)
3. 종료 진행 상황 UI 표시
4. 트레이 아이콘 제거 확인 메커니즘
5. 종료 실패 시 사용자에게 알림

## 관련 파일
- `CustomWindow\CustomWindow\Utility\BorderService.cs`
- `CustomWindow\BorderService_test_winrt2\BorderService_test_winrt2\Tray.cpp`
- `CustomWindow\CustomWindow\ViewModels\NormalSettingsViewModel.cs`
- `CustomWindow\CustomWindow\App.xaml.cs`

## 참조
- 이전 개선: 창 모서리 복원 기능 (`WINDOW_CORNER_RESTORATION_IMPROVEMENTS.md`)
