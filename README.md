# CustomWindow

윈도우 창 테두리를 간단히 커스터마이즈하는 앱입니다. WinUI 3(.NET 8) GUI와 C++(D2D/DComp/DWM) 오버레이 서비스로 구성됩니다.

- GUI: CustomWindow (WinUI 3 / Windows App SDK)
- 서비스: BorderService_test_winrt2 (C++20, D2D/DComp/DWM)

## 주요 기능
- 창 테두리 색/두께/모서리 라운드 설정
- 자동 추적(AutoWindowChange)으로 활성/표시 창 테두리 갱신
- 렌더 모드: Auto / Dwm / DComp
- 트레이 최소화, 부팅 시 자동 실행

## 요구 사항
- Windows 10 20H1(19041)+, Visual Studio 2022
- NuGet: Microsoft.WindowsAppSDK 1.7+, Microsoft.Windows.SDK.BuildTools

## 빌드
1) 솔루션을 VS2022에서 엽니다.
2) 구성 x64 선택 → NuGet 복원 → 전체 빌드.

## 실행/사용
- CustomWindow를 실행하고 Normal 탭에서 설정을 변경합니다.
- 트레이 메뉴로 창 표시/복원, 자동 실행 설정을 관리합니다.

## 서비스 CLI
- --console: 콘솔 창 표시
- --mode {auto|dwm|dcomp}: 렌더 모드
- --color #RRGGBB | #AARRGGBB: 테두리 색상
- --thickness N: 테두리 두께(float)

## 프로젝트 구성(요약)
- CustomWindow/CustomWindow: C# WinUI 3, 설정/트레이/서비스 제어
- CustomWindow/BorderService_test_winrt2: C++20, D3D/D2D/DComp 오버레이

## 참고
- MinimizeToTray가 켜진 경우 닫기(X)는 트레이로 최소화됩니다.
- Windows 11 등 환경에 따라 DWM이 불가하면 DComp로 대체될 수 있습니다.
- 자동 실행은 HKCU\Software\Microsoft\Windows\CurrentVersion\Run에 등록됩니다.