# BorderService 성능 비교 테스트

이 테스트는 두 가지 창 모서리 렌더링 방식의 성능을 비교합니다.

## 비교 대상

### 1. BorderService_Test_winrt (개별 창 방식)
- **위치**: `C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt`
- **렌더링 방식**: 각 창마다 개별 WindowBorder 객체 생성
- **기술**: Direct2D HwndRenderTarget
- **특징**: 
  - 창별로 독립적인 렌더 타겟
  - 각 창의 프레임을 개별적으로 그림
  - 타이머 기반 갱신 (100ms)

### 2. BorderService_test_winrt2 (DirectComposition 오버레이)
- **위치**: `CustomWindow\BorderService_test_winrt2`
- **렌더링 방식**: 전체 화면 투명 오버레이 윈도우
- **기술**: DirectComposition + Direct2D DeviceContext
- **특징**:
  - 단일 전체 화면 DComp Surface
  - 모든 창의 테두리를 한 번에 그림
  - WinEvent 훅 기반 갱신

## 측정 항목

- **CPU 사용률**: 평균, 최대, 최소
- **메모리 사용량**: Private Bytes (평균, 최대, 최소)
- **Working Set**: 평균, 최대
- **CPU 사이클**: 총 사용된 CPU 사이클 수

## 테스트 실행 방법

### 사전 준비

1. **BorderService_Test_winrt 빌드**
   ```powershell
   # Visual Studio에서 Release 모드로 빌드
   # 또는 MSBuild 사용
   msbuild "C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt.sln" /p:Configuration=Release /p:Platform=x64
   ```

2. **BorderService_test_winrt2 빌드**
   ```powershell
   # Visual Studio에서 Release 모드로 빌드
   # 또는 MSBuild 사용
   msbuild "CustomWindow\BorderService_test_winrt2\BorderService_test_winrt2.sln" /p:Configuration=Release /p:Platform=x64
   ```

### 테스트 실행

```powershell
# 프로젝트 루트에서 실행
dotnet test CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj --filter "DisplayName~성능 비교"

# 또는 상세 출력 보기
dotnet test CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj --filter "DisplayName~성능 비교" --logger "console;verbosity=detailed"
```

### Visual Studio에서 실행

1. Test Explorer 열기 (Test > Test Explorer)
2. "성능 비교: BorderService_Test_winrt vs BorderService_test_winrt2" 테스트 찾기
3. 우클릭 > Run

## 테스트 시나리오

1. **워밍업 단계** (2초)
   - 프로세스 시작 및 초기화
   - 렌더링 파이프라인 준비

2. **측정 단계** (10초)
   - 100ms 간격으로 성능 메트릭 샘플링
   - CPU 사용률, 메모리 사용량 기록

3. **분석 단계**
   - 샘플 데이터 통계 계산
   - 두 방식 비교 및 우수성 판단

## 예상 결과

테스트 출력 예시:

```
=== 창 모서리 렌더링 방식 성능 비교 테스트 ===

--- BorderService_Test_winrt (개별 창 D2D HwndRenderTarget) ---
실행 파일: C:\Users\subin\source\repos\BorderService_Test_winrt\...\BorderService_Test_winrt.exe
프로세스 시작됨 (PID: 12345)
워밍업 중... (2000ms)
성능 측정 시작 (10000ms)...
측정 완료 (샘플 수: 100)

?? BorderService_Test_winrt 성능 측정 결과:
   측정 시간: 10,000ms
   샘플 수: 100
   
   CPU 사용률:
      평균: 2.50%
      최대: 5.20%
      최소: 1.10%
   
   메모리 사용량 (Private):
      평균: 45.32 MB
      최대: 48.75 MB
      최소: 42.10 MB
   
   Working Set:
      평균: 52.40 MB
      최대: 55.20 MB

--- BorderService_test_winrt2 (DirectComposition 전체 화면 오버레이) ---
...

┌─────────────────────────────────────────────────────────────────────┐
│                        성능 비교 요약                                │
└─────────────────────────────────────────────────────────────────────┘

   평균 CPU 사용률:
      Test_winrt:   2.50 %
      test_winrt2:  1.80 %
      차이:         -28.0%
      우수:         ? test_winrt2

   ...

?? 전체 평가:
   BorderService_Test_winrt (개별 창): 1점
   BorderService_test_winrt2 (DComp 오버레이): 2점
   
   ? BorderService_test_winrt2 방식이 더 효율적입니다.
```

## 성능 최적화 고려사항

### BorderService_Test_winrt 장점
- 각 창이 독립적으로 관리됨
- 특정 창만 업데이트할 수 있음
- 작은 영역만 다시 그리기 가능

### BorderService_Test_winrt 단점
- 창이 많을수록 리소스 사용 증가 (창당 HwndRenderTarget)
- 타이머 기반이라 불필요한 갱신 가능
- 각 창마다 메모리 오버헤드

### BorderService_test_winrt2 장점
- 단일 DComp Surface로 효율적
- WinEvent 기반으로 필요할 때만 갱신
- 창 수에 관계없이 일정한 메모리 사용

### BorderService_test_winrt2 단점
- 전체 화면을 다시 그려야 함
- 부분 업데이트 어려움
- 큰 화면에서 메모리 사용 증가 가능

## 문제 해결

### 실행 파일을 찾을 수 없는 경우

테스트에서 다음과 같은 메시지가 나타나면:
```
?? BorderService_Test_winrt 실행 파일을 찾을 수 없습니다.
```

해결 방법:
1. 해당 프로젝트를 Release 모드로 빌드했는지 확인
2. `BorderServicePerformanceTests.cs` 파일에서 경로 상수를 실제 경로로 수정
3. x64 플랫폼으로 빌드했는지 확인

### 프로세스가 예기치 않게 종료되는 경우

- 실행 파일이 정상적으로 동작하는지 먼저 수동으로 실행해 확인
- 필요한 DLL이 모두 있는지 확인 (VC++ Redistributable 등)
- 이벤트 뷰어에서 오류 로그 확인

## 추가 개선 사항

향후 추가할 수 있는 측정 항목:

1. **GPU 사용률**: GPU 성능 카운터 사용
2. **프레임 타이밍**: 렌더링 프레임당 소요 시간
3. **윈도우 수 변화**: 창 개수에 따른 성능 변화
4. **배터리 영향**: 노트북에서 배터리 소모량
5. **화면 크기 영향**: 다양한 해상도에서 테스트

## 라이선스

이 테스트 코드는 CustomWindow 프로젝트의 라이선스를 따릅니다.
