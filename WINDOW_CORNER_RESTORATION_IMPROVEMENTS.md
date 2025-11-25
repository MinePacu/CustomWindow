# 창 모서리 복원 기능 개선

## 개요
DWM 모드에서 윈도우 설정 자동 변경을 끄게 되면 창 모서리를 원래대로 복원하는 로직이 추가되었습니다.

## 변경 사항

### 1. NormalSettingsViewModel.cs
**파일 경로**: `CustomWindow\CustomWindow\ViewModels\NormalSettingsViewModel.cs`

#### 주요 변경사항:

##### A. `AutoWindowChange` 속성 개선
- `AutoWindowChange`를 `false`로 설정할 때 창 모서리를 기본 상태로 복원하는 로직 추가
- 이전: BorderService 중지만 수행
- 이후: BorderService 중지 + 창 모서리 기본 상태 복원

```csharp
else
{
    // AutoWindowChange OFF: 서비스 중지 및 원래 상태로 복원
    BorderService.StopIfRunning();
    WindowStyleApplier.Stop();
    
    // DWM 모드에서 창 모서리를 기본 상태로 복원
    RestoreWindowCornersToDefault();
    
    WindowTracker.Stop();
    WindowTracker.AddExternalLog("AutoWindowChange OFF: BorderService 중지 및 창 설정 복원");
}
```

##### B. `RestoreWindowCornersToDefault()` 메서드 추가
새로운 private 메서드로, 모든 창의 모서리 설정을 기본 상태로 복원합니다.

**기능**:
- Windows 11 이상에서만 작동 (Windows 10은 창 모서리 커스터마이징 미지원)
- `WindowTracker`에서 현재 추적 중인 모든 창 목록을 가져옴
- 각 창에 대해 `DwmWindowManager.SetCornerPreference(hwnd, "기본")` 호출
- 성공/실패 횟수를 로그에 기록

**특징**:
- 안전한 오류 처리 (개별 창 복원 실패 시에도 계속 진행)
- 상세한 로깅 (복원 진행 상황 추적 가능)

### 2. App.xaml.cs
**파일 경로**: `CustomWindow\CustomWindow\App.xaml.cs`

#### 주요 변경사항:

##### A. 애플리케이션 종료 시 복원 로직 추가
`ProcessExit` 및 `Window.Closed` 이벤트 핸들러에 창 모서리 복원 로직 추가:

```csharp
// 종료 시 기본 설정 복원이 활성화된 경우 창 모서리 복원
if (ConfigStore?.Config.RestoreDefaultsOnExit == true)
{
    RestoreWindowCornersOnExit();
    WindowTracker.AddExternalLog("App exit: 창 모서리 기본 상태로 복원");
}
```

##### B. `RestoreWindowCornersOnExit()` 메서드 추가
애플리케이션 종료 시 모든 창의 모서리를 기본 상태로 복원하는 static 메서드:

**기능**:
- `RestoreDefaultsOnExit` 설정이 활성화된 경우에만 실행
- Windows 11 이상에서만 작동
- 모든 추적 중인 창의 모서리를 "기본"으로 설정
- 조용한 실패 처리 (종료 시 예외가 발생해도 무시)

## 동작 흐름

### 시나리오 1: AutoWindowChange 토글 OFF
1. 사용자가 "자동 창 설정 변경" 토글을 OFF로 전환
2. `NormalSettingsViewModel.AutoWindowChange` setter 호출
3. BorderService 및 WindowStyleApplier 중지
4. `RestoreWindowCornersToDefault()` 호출
5. WindowTracker에서 현재 추적 중인 모든 창 목록 획득
6. 각 창에 대해 DWM API를 통해 모서리 설정을 "기본"으로 복원
7. 복원 결과 로그 출력

### 시나리오 2: 애플리케이션 종료
1. 사용자가 애플리케이션 종료
2. `ProcessExit` 또는 `Window.Closed` 이벤트 발생
3. `RestoreDefaultsOnExit` 설정 확인
4. 설정이 활성화되어 있으면 `RestoreWindowCornersOnExit()` 호출
5. 모든 추적 중인 창의 모서리를 "기본"으로 복원
6. 기타 정리 작업 수행 (BorderService 중지 등)

## 기술적 세부사항

### Windows 버전 확인
- `DwmWindowManager.SupportsCustomCaptionColors()` 사용
- Windows 11 (빌드 22000) 이상에서만 창 모서리 커스터마이징 지원
- Windows 10에서는 복원 로직을 건너뜀

### DWM API 활용
- `DWMWA_WINDOW_CORNER_PREFERENCE` 속성 사용
- `DWMWCP_DEFAULT` 값으로 설정하여 시스템 기본 동작으로 복원
- `DwmSetWindowAttribute` Win32 API 호출

### 안전성
- 각 창별 독립적인 오류 처리
- 종료 시 조용한 실패 (silent fail) 처리
- 로깅을 통한 문제 추적 가능

## 사용자 경험 개선

### 이전 동작
- AutoWindowChange를 끄면 BorderService만 중지
- 이미 적용된 창 모서리 스타일은 그대로 유지됨
- 사용자가 수동으로 각 창을 재시작해야 원래 상태로 복원

### 개선된 동작
- AutoWindowChange를 끄면 자동으로 모든 창의 모서리가 기본 상태로 복원
- 애플리케이션 종료 시에도 자동 복원 (설정 활성화 시)
- 즉각적인 시각적 피드백
- 수동 작업 불필요

## 로깅
다음과 같은 로그 메시지가 추가됨:
- "AutoWindowChange OFF: BorderService 중지 및 창 설정 복원"
- "Windows 10에서는 창 모서리 복원이 필요하지 않습니다"
- "복원할 창이 없습니다"
- "창 모서리 기본 상태로 복원 완료: X/Y개 창"
- "App exit: 창 모서리 기본 상태로 복원"
- "Window closed: 창 모서리 기본 상태로 복원"

## 테스트 권장사항

### 테스트 케이스
1. **기본 토글 테스트**
   - AutoWindowChange ON -> 창 모서리 스타일 변경 -> AutoWindowChange OFF
   - 확인: 모든 창의 모서리가 기본 상태로 복원되는지

2. **Windows 버전 테스트**
   - Windows 10에서 실행 시 복원 로직이 안전하게 건너뛰어지는지
   - Windows 11에서 정상 작동하는지

3. **종료 시 복원 테스트**
   - RestoreDefaultsOnExit 활성화 -> 애플리케이션 종료
   - 확인: 창 모서리가 기본 상태로 복원되는지

4. **예외 상황 테스트**
   - 창이 없는 상태에서 AutoWindowChange OFF
   - 창이 이미 닫힌 상태에서 복원 시도

## 호환성
- Windows 10: 복원 로직 건너뜀 (정상 동작)
- Windows 11: 완전한 기능 지원
- .NET 8 호환
- C# 12.0 호환

## 향후 개선 가능성
1. 창별 원래 모서리 스타일 저장 및 복원 (현재는 모두 "기본"으로 복원)
2. 복원 속도 최적화 (병렬 처리)
3. UI에 복원 진행 상황 표시
4. 선택적 창 복원 기능 (특정 창만 복원)
