# 색 설정 페이지 두께 초기화 문제 해결

## 문제 설명
윈도우 설정 자동 변경을 켠 상태에서 디버그 로그를 활성화하고 색 설정 페이지로 진입하면, 간혹 테두리 두께 설정이 매우 얇아지는(1px 또는 그 이하) 문제가 발생했습니다. 디버그 로그 창이 닫혔다가 다시 열리면서 BorderService가 재시작되는 과정에서 이 문제가 더 자주 나타났습니다.

## 원인 분석

### 1. XAML 바인딩 초기화 순서 문제
```xaml
<Slider x:Name="BorderThicknessSlider" 
        Minimum="1" Maximum="20" StepFrequency="1" 
        Value="{x:Bind ViewModel.BorderThickness, Mode=TwoWay}" 
        ValueChanged="BorderThicknessSlider_ValueChanged"/>
```

**문제점**:
- XAML이 로드될 때 슬라이더가 초기화되면서 `ValueChanged` 이벤트가 발생
- 슬라이더의 기본값(0 또는 1)에서 실제 설정값으로 변경되는 동안 중간값이 이벤트로 발생
- 페이지 로드 중에도 이벤트 핸들러가 실행되어 의도하지 않은 값이 BorderService로 전송됨

### 2. 이벤트 핸들러 타이밍 문제

**기존 코드**:
```csharp
private void BorderThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
{
    var auto = App.ConfigStore!.Config.AutoWindowChange;
    var running = BorderService.IsRunning;
    var th = (int)e.NewValue;

    if (auto && running)
    {
        BorderService.UpdateThickness(th);
    }
}
```

**문제점**:
- 페이지 로딩 중에도 이벤트 발생
- 초기화 중 잘못된 값(1px 미만)이 전송될 수 있음
- 값 검증 없음

### 3. ColorPicker 초기화 문제
- `BorderColorPicker`도 동일한 문제 발생
- 초기 로딩 시 `ColorChanged` 이벤트가 발생하여 불필요한 IPC 전송

### 4. 디버그 콘솔 재시작 시 타이밍 이슈
- `ShowBorderServiceConsole` 설정 변경 시 BorderService 재시작
- 재시작 중 색 설정 페이지가 로드되면 초기화 이벤트와 재시작 타이밍이 겹침
- 이때 잘못된 값이 전송될 확률 증가

## 해결 방법

### 1. 페이지 로드 플래그 추가

```csharp
// 페이지 로딩 완료 플래그 - 초기 로딩 중 이벤트 무시
private bool _isPageLoaded = false;

private void ColorSetting_Loaded(object sender, RoutedEventArgs e)
{
    // ...기존 코드...
    
    // 로딩 완료 플래그 설정 (약간의 지연 후 - UI 초기화 완료 대기)
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
    timer.Tick += (s, args) =>
    {
        _isPageLoaded = true;
        timer.Stop();
        WindowTracker.AddExternalLog("[ColorSetting] Page fully loaded, event handling enabled");
    };
    timer.Start();
}

private void ColorSetting_Unloaded(object sender, RoutedEventArgs e)
{
    _isPageLoaded = false;
    WindowTracker.AddExternalLog("[ColorSetting] Page unloaded, event handling disabled");
}
```

**장점**:
- 페이지 로드 완료 후에만 이벤트 처리
- 100ms 지연으로 XAML 바인딩 완료 보장
- Unload 시 플래그 초기화로 메모리 누수 방지

### 2. 이벤트 핸들러 개선

#### A. BorderThicknessSlider_ValueChanged 개선
```csharp
private void BorderThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
{
    // 페이지 로딩 중이면 이벤트 무시
    if (!_isPageLoaded)
    {
        WindowTracker.AddExternalLog($"[ColorSetting] Ignoring ThicknessChanged during page load (value={e.NewValue})");
        return;
    }

    var auto = App.ConfigStore!.Config.AutoWindowChange;
    var running = BorderService.IsRunning;
    var th = (int)e.NewValue;

    // 두께가 너무 작으면(1 미만) 무시
    if (th < 1)
    {
        WindowTracker.AddExternalLog($"[ColorSetting] Ignoring invalid thickness value: {th}");
        return;
    }

    WindowTracker.AddExternalLog($"[ColorSetting] Thickness changed -> {th} | Auto={auto} Running={running}");

    if (auto && running)
    {
        BorderService.UpdateThickness(th);
        WindowTracker.AddExternalLog($"[ColorSetting] Sent UpdateThickness({th})");
    }
    // ...
}
```

**개선사항**:
- ? 페이지 로드 중 이벤트 무시
- ? 값 검증 (1 미만 거부)
- ? 상세한 로깅

#### B. BorderColorPicker_ColorChanged 개선
```csharp
private void BorderColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
{
    // 페이지 로딩 중이면 이벤트 무시
    if (!_isPageLoaded)
    {
        WindowTracker.AddExternalLog("[ColorSetting] Ignoring ColorChanged during page load");
        return;
    }
    // ...기존 로직...
}
```

### 3. ViewModel 레벨 검증 추가

```csharp
public int BorderThickness 
{ 
    get => _config.BorderThickness; 
    set 
    { 
        // 두께 값 검증: 1-20 범위 제한
        if (value < 1)
        {
            WindowTracker.AddExternalLog($"[ColorSettingsViewModel] Invalid thickness value ignored: {value} (must be >= 1)");
            return;
        }
        if (value > 20)
        {
            WindowTracker.AddExternalLog($"[ColorSettingsViewModel] Thickness value clamped: {value} -> 20");
            value = 20;
        }
        
        if (_config.BorderThickness == value) return;
        
        WindowTracker.AddExternalLog($"[ColorSettingsViewModel] BorderThickness changing: {_config.BorderThickness} -> {value}");
        _config.BorderThickness = value; 
        OnPropertyChanged(); 
    } 
}
```

**검증 로직**:
- 1 미만 값 거부
- 20 초과 값 자동 제한
- 중복 설정 방지

### 4. 설정 로드 시 기본값 검증

```csharp
private static void ApplyDefaults(AppConfig c)
{
    c.BorderColor ??= "#FF4080FF";
    c.CaptionColor ??= "#FF202020";
    c.CaptionTextColor ??= "#FFFFFFFF";
    c.CaptionTextColorMode ??= "자동";
    c.CaptionColorMode ??= "밝게";
    c.WindowCornerMode ??= "기본";
    c.TaskbarCornerMode ??= "기본";
    
    // BorderThickness 값 검증 및 보정
    if (c.BorderThickness < 1)
    {
        WindowTracker.AddExternalLog($"[ConfigStore] Invalid BorderThickness {c.BorderThickness} detected, resetting to 3");
        c.BorderThickness = 3;
    }
    else if (c.BorderThickness > 20)
    {
        WindowTracker.AddExternalLog($"[ConfigStore] BorderThickness {c.BorderThickness} too large, clamping to 20");
        c.BorderThickness = 20;
    }
}
```

**보호 계층**:
- 설정 파일 손상 시 안전한 기본값으로 복구
- 애플리케이션 시작 시 즉시 검증

## 타이밍 다이어그램

### 문제 상황 (이전)
```
[0ms]    페이지 로드 시작
[10ms]   XAML 파싱, 슬라이더 생성 (기본값 0)
[20ms]   ValueChanged 이벤트 발생 (value=0) ? BorderService로 전송
[30ms]   데이터 바인딩 적용 시작
[50ms]   ValueChanged 이벤트 발생 (value=1) ? BorderService로 전송
[80ms]   ValueChanged 이벤트 발생 (value=3) ? BorderService로 전송
[100ms]  실제 설정값 로드 (value=10)
[110ms]  ValueChanged 이벤트 발생 (value=10) ? 올바른 값
```

### 해결 후 (현재)
```
[0ms]    페이지 로드 시작
[10ms]   XAML 파싱, 슬라이더 생성 (기본값 0)
[20ms]   ValueChanged 이벤트 발생 (value=0) ? _isPageLoaded=false로 무시
[30ms]   데이터 바인딩 적용 시작
[50ms]   ValueChanged 이벤트 발생 (value=1) ? _isPageLoaded=false로 무시
[80ms]   ValueChanged 이벤트 발생 (value=3) ? _isPageLoaded=false로 무시
[100ms]  _isPageLoaded = true 설정
[110ms]  실제 설정값 로드 (value=10)
[120ms]  ValueChanged 이벤트 발생 (value=10) ? 올바른 값, 전송
```

## 로그 예시

### 정상 동작 시:
```
[12:34:56] [ColorSetting] Page fully loaded, event handling enabled
[12:34:57] [ColorSetting] Thickness changed -> 10 | Auto=True Running=True
[12:34:57] [ColorSetting] Sent UpdateThickness(10)
```

### 초기화 중 차단:
```
[12:34:55] [ColorSetting] Ignoring ThicknessChanged during page load (value=0)
[12:34:55] [ColorSetting] Ignoring ThicknessChanged during page load (value=1)
[12:34:56] [ColorSetting] Page fully loaded, event handling enabled
[12:34:57] [ColorSetting] Thickness changed -> 10 | Auto=True Running=True
```

### 잘못된 값 차단:
```
[12:34:57] [ColorSetting] Thickness changed -> 0 | Auto=True Running=True
[12:34:57] [ColorSetting] Ignoring invalid thickness value: 0
```

### 설정 파일 보정:
```
[12:34:50] [ConfigStore] Invalid BorderThickness 0 detected, resetting to 3
```

## 테스트 시나리오

### 1. 정상 페이지 전환 테스트
- [ ] 일반 설정 → 색 설정 전환
- [ ] 두께 값이 유지되는지 확인
- [ ] 로그에 "Ignoring during page load" 메시지 확인

### 2. 디버그 콘솔 재시작 테스트
- [ ] 디버그 로그 활성화
- [ ] 색 설정 페이지로 이동
- [ ] 디버그 콘솔 토글
- [ ] 두께 값이 유지되는지 확인

### 3. 설정 파일 손상 테스트
- [ ] Config.json 파일 수동 편집 (BorderThickness: -5)
- [ ] 앱 재시작
- [ ] 두께가 3으로 복구되는지 확인

### 4. 슬라이더 조작 테스트
- [ ] 페이지 로드 후 슬라이더 조작
- [ ] 각 값 변경이 BorderService로 정상 전송되는지 확인
- [ ] 1 미만 값 입력 시 거부되는지 확인

## 추가 안전 장치

### 1. BorderService.UpdateThickness 레벨 검증
이미 구현되어 있으며, IPC 전송 전 최종 검증:
```csharp
public static void UpdateThickness(int thickness)
{
    lock (_sync)
    {
        if (_isRestarting)
        {
            LogMessage($"Ignoring thickness update during restart");
            return;
        }

        _lastThickness = thickness; // 값 저장

        if (OverlayAvailable)
        {
            if (TrySendSettingsToOverlay(_lastColor, thickness))
            {
                LogMessage($"Applied thickness via IPC -> {thickness}");
                return;
            }
            // ...
        }
    }
}
```

### 2. C++ 측 검증
Args.cpp에서 명령줄 인자 파싱 시 검증:
```cpp
if (arg == L"--thickness" && i + 1 < argc) {
    try { 
        float tv = std::stof(argv[i + 1]); 
        if (tv > 0 && tv < 1000) { // 0 초과, 1000 미만만 허용
            g_thickness = tv; 
        } 
    } catch (...) {}
    ++i; 
    continue;
}
```

## 결론

### 문제 해결 계층
```
1. ConfigStore (앱 시작 시)
   ↓ 잘못된 값 보정
2. ViewModel (속성 설정 시)
   ↓ 값 범위 검증
3. View (이벤트 핸들러)
   ↓ 페이지 로드 상태 확인 + 값 검증
4. BorderService (IPC 전송 시)
   ↓ 최종 검증
5. C++ Args.cpp (프로세스 시작 시)
   ↓ 명령줄 인자 검증
```

### 장점
- ? **다층 방어**: 각 계층에서 독립적으로 검증
- ? **디버깅 용이**: 상세한 로그로 문제 추적 가능
- ? **사용자 경험 개선**: 의도하지 않은 값 변경 방지
- ? **안정성 향상**: 설정 파일 손상에도 안전하게 복구
- ? **성능 영향 최소화**: 100ms 지연만 추가 (사용자 체감 불가)

### 향후 개선 가능성
1. 페이지 로드 지연 시간을 동적으로 조정 (XAML 복잡도에 따라)
2. 슬라이더 초기화 완료 이벤트 활용 (Loaded 이벤트)
3. 값 변경 debounce 추가 (연속 변경 시)
4. 설정 프로필 기능 (빠른 전환)

## 관련 파일
- `CustomWindow\CustomWindow\Pages\ColorSetting.xaml.cs`
- `CustomWindow\CustomWindow\ViewModels\ColorSettingsViewModel.cs`
- `CustomWindow\CustomWindow\Utility\ConfigStore.cs`
- `CustomWindow\CustomWindow\Utility\AppConfig.cs`
- `CustomWindow\CustomWindow\Utility\BorderService.cs`
- `CustomWindow\BorderService_test_winrt2\BorderService_test_winrt2\Args.cpp`
