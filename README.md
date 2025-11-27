# CustomWindow

윈도우 테두리를 커스터마이징하는 도구입니다. WinUI 3(.NET 8) 기반 GUI와 C++(D2D/DComp/DWM) 렌더링 서비스를 사용하여 고성능 오버레이를 제공합니다.

## 주요 기능 (Features)
*   **테두리 커스터마이징**: 창 테두리의 색상, 두께, 둥근 모서리 정도를 설정할 수 있습니다.
*   **자동 감지**: 활성/비활성 창을 자동으로 감지하여 테두리를 적용합니다 (AutoWindowChange).
*   **다양한 렌더링 모드**: Auto, DWM, DComp 모드를 지원하여 호환성을 극대화했습니다.
*   **편의 기능**: 시스템 트레이 최소화, 윈도우 시작 시 자동 실행을 지원합니다.

## 시스템 요구 사항 (Requirements)
*   Windows 10 20H1 (Build 19041) 이상
*   Visual Studio 2022
*   **NuGet 패키지**:
    *   Microsoft.WindowsAppSDK 1.7+
    *   Microsoft.Windows.SDK.BuildTools

## 빌드 방법 (Build)
1.  Visual Studio 2022에서 솔루션 파일(`.sln`)을 엽니다.
2.  솔루션 탐색기에서 NuGet 패키지를 복원합니다.
3.  플랫폼 대상을 `x64`로 설정하고 솔루션을 빌드합니다.

## 사용법 (Usage)
*   프로그램을 실행하면 시스템 트레이 아이콘으로 상주합니다 (`CustomWindow`).
*   트레이 아이콘 우클릭 메뉴를 통해 설정을 변경하거나 프로그램을 종료할 수 있습니다.
*   창 닫기(X) 버튼을 누르면 트레이로 최소화됩니다.

### CLI 옵션 (Command Line Arguments)
| 옵션 | 설명 |
| --- | --- |
| `--console` | 디버그용 콘솔 창을 표시합니다. |
| `--mode {auto\|dwm\|dcomp}` | 렌더링 모드를 강제로 설정합니다. |
| `--color #RRGGBB` | 테두리 색상을 지정합니다 (예: `#FF0000`). 알파값 포함 시 `#AARRGGBB`. |
| `--thickness N` | 테두리 두께를 `N` (float) 픽셀로 지정합니다. |

## 프로젝트 구조 (Project Structure)
*   **CustomWindow/CustomWindow**: C# WinUI 3 기반의 GUI 프로젝트. 트레이 아이콘 및 설정 UI, 프로세스 관리를 담당합니다.
*   **CustomWindow/BorderService_test_winrt2**: C++20 기반의 렌더링 코어. D3D, D2D, DirectComposition을 사용하여 테두리를 그립니다.

## 참고 사항 (Notes)
*   Windows 11 환경 등에서 DWM 방식이 불가능할 경우 자동으로 DComp 방식으로 전환됩니다.
*   자동 실행 설정 시 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 레지스트리에 등록됩니다.