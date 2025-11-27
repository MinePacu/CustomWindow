# CustomWindow

**CustomWindow**는 Windows 애플리케이션의 창 테두리를 사용자가 원하는 대로 커스터마이즈할 수 있는 유틸리티입니다. WinUI 3 기반의 직관적인 설정 UI와 C++ 기반의 고성능 오버레이 서비스를 제공합니다.

## ✨ 주요 기능

*   **테두리 커스터마이즈**: 색상, 두께, 모서리 둥글기 등을 자유롭게 설정할 수 있습니다.
*   **자동 감지 (AutoWindowChange)**: 현재 활성화된 창이나 표시 중인 창을 자동으로 감지하여 테두리를 적용합니다.
*   **다양한 렌더링 모드**: Auto, DWM, DComp 모드를 지원하여 시스템 환경에 최적화된 렌더링을 제공합니다.
*   **편의 기능**: 시스템 트레이 최소화 및 윈도우 시작 시 자동 실행을 지원합니다.

## 🛠️ 기술 스택

이 프로젝트는 두 가지 주요 컴포넌트로 구성되어 있습니다.

*   **GUI (CustomWindow)**: WinUI 3 (Windows App SDK) 및 .NET 8을 사용하여 제작된 설정 관리 인터페이스입니다.
*   **Service (BorderService_test_winrt2)**: C++20, Direct2D, DirectComposition, DWM API를 활용하여 제작된 고성능 오버레이 서비스입니다.

## 📋 요구 사항 (Requirements)

*   **운영체제**: Windows 10 20H1 (Build 19041) 이상
*   **개발 도구**: Visual Studio 2022
*   **필수 패키지**:
    *   Microsoft.WindowsAppSDK 1.7 이상
    *   Microsoft.Windows.SDK.BuildTools

## 🚀 빌드 및 설치 (Build)

1.  Visual Studio 2022에서 솔루션 파일(`CustomWindow.sln`)을 엽니다.
2.  솔루션 구성을 **x64**로 선택합니다.
3.  NuGet 패키지 복원을 수행합니다.
4.  전체 솔루션을 빌드합니다.

## 📖 사용 방법 (Usage)

1.  **CustomWindow** 앱을 실행합니다.
2.  **Normal** 탭에서 원하는 테두리 스타일을 설정합니다.
3.  설정이 완료되면 자동으로 서비스가 테두리를 그립니다.
4.  앱을 닫아도 시스템 트레이에서 백그라운드로 실행됩니다.

### 서비스 CLI 옵션

고급 사용자를 위해 백그라운드 서비스(`BorderService`)는 커맨드 라인 인자를 지원합니다.

*   `--console`: 디버깅용 콘솔 창을 표시합니다.
*   `--mode {auto|dwm|dcomp}`: 렌더링 모드를 강제로 지정합니다.
*   `--color #RRGGBB` 또는 `#AARRGGBB`: 테두리 색상을 지정합니다.
*   `--thickness N`: 테두리 두께를 `float` 단위로 지정합니다.

## 📂 프로젝트 구조

*   `CustomWindow/CustomWindow`: C# WinUI 3 프로젝트 (UI, 트레이, 서비스 제어 로직)
*   `CustomWindow/BorderService_test_winrt2`: C++20 프로젝트 (오버레이 렌더링 엔진)

## ⚠️ 참고 사항

*   **최소화**: `MinimizeToTray` 옵션이 활성화된 경우, 창 닫기(X) 버튼을 누르면 프로그램이 종료되지 않고 시스템 트레이로 최소화됩니다.
*   **렌더링 호환성**: Windows 11 등 특정 환경에서 DWM 방식이 지원되지 않는 경우, 자동으로 DComp 방식으로 전환될 수 있습니다.
*   **자동 실행**: 자동 실행 옵션 활성화 시 레지스트리 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 경로에 등록됩니다.
