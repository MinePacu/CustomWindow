# BorderService 성능 비교 테스트 - 종합 가이드

두 가지 창 모서리 렌더링 방식의 CPU/메모리/GPU 사용량 등의 성능을 비교하는 테스트 도구입니다.

## ?? 목차

1. [개요](#개요)
2. [비교 대상](#비교-대상)
3. [테스트 종류](#테스트-종류)
4. [빠른 시작](#빠른-시작)
5. [그래프 생성](#그래프-생성)
6. [상세 사용법](#상세-사용법)
7. [측정 항목](#측정-항목)
8. [결과 해석](#결과-해석)
9. [문제 해결](#문제-해결)

## 개요

이 테스트 도구는 두 가지 다른 기술로 구현된 창 테두리 렌더링 방식의 성능을 정량적으로 비교하고, 결과를 그래프로 시각화합니다.

### 비교 대상

| 방식 | 경로 | 기술 스택 |
|------|------|-----------|
| **BorderService_Test_winrt** | `C:\Users\subin\source\repos\BorderService_Test_winrt` | 개별 창 방식 (D2D HwndRenderTarget) |
| **BorderService_test_winrt2** | `CustomWindow\BorderService_test_winrt2` | DirectComposition 오버레이 |

### 주요 차이점

#### BorderService_Test_winrt
- ? 각 창마다 독립적인 WindowBorder 객체
- ? Direct2D HwndRenderTarget 사용
- ? 타이머 기반 갱신 (100ms 간격)
- ?? 창이 많아질수록 리소스 증가

#### BorderService_test_winrt2
- ? 단일 전체 화면 투명 오버레이
- ? DirectComposition + D2D DeviceContext
- ? WinEvent 훅 기반 갱신
- ?? 전체 화면 다시 그리기 필요

## 테스트 종류

### 1. 기본 성능 비교 테스트
- **파일**: `BorderServicePerformanceTests.cs`
- **측정 시간**: 10초
- **측정 항목**: CPU, 메모리, Working Set, CPU 사이클
- **적합한 경우**: 빠른 기본 비교가 필요할 때

### 2. 고급 성능 비교 테스트 ?
- **파일**: `BorderServiceAdvancedPerformanceTests.cs`
- **측정 시간**: 15초
- **측정 항목**: CPU, 메모리, GPU, 핸들 수, 스레드 수, 표준편차
- **CSV 생성**: 시계열 데이터를 CSV로 저장
- **그래프 생성**: 자동으로 성능 그래프 생성 가능
- **적합한 경우**: 상세한 성능 분석 및 시각화가 필요할 때

### 3. 부하 테스트
- **파일**: `BorderServiceAdvancedPerformanceTests.cs`
- **시나리오**: 여러 창(notepad) 동시 실행
- **측정 항목**: 다중 창 환경에서의 성능
- **적합한 경우**: 실제 사용 환경 시뮬레이션

## 빠른 시작

### ?? 원클릭 실행 (권장)

```powershell
# 테스트 실행 + CSV 생성 + 그래프 생성을 한 번에!
.\RunFullTest.ps1

# 자동으로 그래프를 열어서 확인
.\RunFullTest.ps1 -AutoOpen

# 기존 CSV로 그래프만 재생성
.\RunFullTest.ps1 -SkipTest
```

### 단계별 실행

#### 1단계: 빌드 준비

두 프로젝트를 Release 모드로 빌드합니다.

```powershell
# BorderService_Test_winrt 빌드
cd "C:\Users\subin\source\repos\BorderService_Test_winrt"
msbuild BorderService_Test_winrt.sln /p:Configuration=Release /p:Platform=x64

# BorderService_test_winrt2 빌드
cd "C:\Users\subin\source\repos\MinePacu\CustomWIndow"
msbuild CustomWindow\BorderService_test_winrt2\BorderService_test_winrt2.sln /p:Configuration=Release /p:Platform=x64
```

#### 2단계: 테스트 실행

```powershell
# 대화형 메뉴 (권장)
.\RunPerformanceTests.ps1

# 빠른 테스트
.\QuickTest.ps1

# 고급 테스트 (그래프 생성용)
dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
    --filter "DisplayName~고급 성능 비교"
```

#### 3단계: 그래프 생성 (선택 사항)

```powershell
# 자동 그래프 생성
.\GenerateGraphs.ps1

# 수동 그래프 생성
python plot_performance.py performance_comparison_timeseries.csv
```

## 그래프 생성

### 필요한 환경

```powershell
# Python 설치 확인
python --version

# 필요한 패키지 설치
pip install matplotlib pandas
```

### 생성되는 그래프

1. **비교 그래프** (`*_comparison_timeseries_graph.png`)
   - CPU 사용률 비교 (시간별)
   - 메모리 사용량 비교 (시간별)
   - GPU 사용률 비교 (시간별)

2. **요약 차트** (`*_comparison_timeseries_summary.png`)
   - 평균 CPU, 메모리, GPU 막대 그래프

3. **개별 상세 그래프** (`*_timeseries_graph.png`)
   - CPU, 메모리, Working Set, GPU
   - 핸들 수, 스레드 수

### 그래프 예시

```
?? 생성된 파일:
├── performance_comparison_timeseries_graph.png    # 비교 그래프
├── performance_comparison_timeseries_summary.png  # 요약 차트
├── performance_test_winrt_timeseries_graph.png    # Test_winrt 상세
└── performance_test_winrt2_timeseries_graph.png   # test_winrt2 상세
```

## 상세 사용법

### 테스트 출력 예시

```
=== 고급 창 모서리 렌더링 성능 비교 (GPU 포함) ===

? GPU 성능 카운터 초기화 완료 (2개 어댑터)

--- BorderService_Test_winrt (개별 창 방식) ---
실행: BorderService_Test_winrt.exe
PID: 12345
워밍업 3000ms...
측정 시작 (15000ms)...
? 측정 완료 (75 샘플)
? 시계열 데이터 저장됨: performance_test_winrt_timeseries.csv

?? BorderService_Test_winrt 상세 성능:
   측정 시간: 15,000ms, 샘플: 75
   
   ?? CPU:
      평균: 2.35% (±0.45%)
      범위: 1.20% ~ 4.80%
   
   ?? 메모리:
      Private: 44.50 MB (최대 47.20 MB)
      WorkingSet: 51.30 MB (최대 54.10 MB)
   
   ?? GPU:
      평균: 1.25%
      최대: 3.40%
   
   ?? 리소스:
      핸들: 245 (최대 258)
      스레드: 12.0

--- BorderService_test_winrt2 (DirectComposition 전체 화면 오버레이) ---
실행: BorderService_test_winrt2.exe
PID: 54321
워밍업 3000ms...
측정 시작 (15000ms)...
? 측정 완료 (75 샘플)
? 시계열 데이터 저장됨: performance_test_winrt2_timeseries.csv

?? BorderService_test_winrt2 상세 성능:
   측정 시간: 15,000ms, 샘플: 75
   
   ?? CPU:
      평균: 1.80% (±0.35%)
      범위: 0.90% ~ 3.60%
   
   ?? 메모리:
      Private: 38.50 MB (최대 40.80 MB)
      WorkingSet: 45.10 MB (최대 47.90 MB)
   
   ?? GPU:
      평균: 2.10%
      최대: 5.20%
   
   ?? 리소스:
      핸들: 220 (최대 235)
      스레드: 10.0

? 통합 시계열 데이터 저장됨: performance_comparison_timeseries.csv
  → 그래프 생성: python plot_performance.py performance_comparison_timeseries.csv
```

## 측정 항목

### CPU 관련
- **평균 CPU 사용률**: 측정 기간 동안 평균 CPU 사용량 (%)
- **최대/최소 CPU**: CPU 사용량의 범위
- **표준편차**: CPU 사용의 안정성 (낮을수록 안정적)
- **총 CPU 사이클**: 전체 사용된 CPU 사이클 수

### 메모리 관련
- **Private Bytes**: 프로세스 전용 메모리 (MB)
- **Working Set**: 물리 메모리 사용량 (MB)
- **평균/최대값**: 메모리 사용 추이

### GPU 관련 (고급 테스트)
- **GPU 사용률**: 3D 엔진 사용률 (%)
- ?? 관리자 권한 필요할 수 있음

### 시스템 리소스 (고급 테스트)
- **핸들 수**: 시스템 핸들 사용량
- **스레드 수**: 프로세스의 스레드 개수

### 시계열 데이터 (CSV)
- **시간별 추이**: 0.2초 간격으로 샘플링
- **그래프 생성**: Python matplotlib로 시각화

## 결과 해석

### 성능 비교 기준

| 차이 | 판정 |
|------|------|
| < 5% | 동등 (?) |
| 5-20% | 약간 우수 (?) |
| > 20% | 확실히 우수 (??) |

### 일반적인 예상 결과

#### DirectComposition 방식 (test_winrt2)이 유리한 경우:
- 창이 많을 때 (5개 이상)
- 정적인 화면 (창이 자주 움직이지 않을 때)
- 메모리 효율 중요할 때

#### 개별 창 방식 (Test_winrt)이 유리한 경우:
- 창이 적을 때 (1-3개)
- 특정 창만 자주 업데이트될 때
- 부분 렌더링이 중요할 때

### 그래프 해석 팁

?? **CPU 사용률 그래프**
- 평탄한 선: 안정적인 성능
- 급격한 스파이크: 최적화 필요
- 낮은 평균: 효율적

?? **메모리 사용량 그래프**
- 수평선: 메모리 누수 없음
- 우상향: 메모리 누수 의심
- 낮은 값: 효율적

?? **GPU 사용률 그래프**
- 낮은 사용률: CPU 기반 렌더링
- 높은 사용률: GPU 가속 활용

## 문제 해결

### 실행 파일을 찾을 수 없는 경우

**증상**: `?? BorderService_XXX 실행 파일을 찾을 수 없습니다.`

**해결**:
1. Release 모드로 빌드했는지 확인
2. x64 플랫폼으로 빌드했는지 확인
3. 테스트 파일에서 경로 상수 수정:
   ```csharp
   private const string BorderServiceTestWinrtPath = @"실제_경로";
   ```

### GPU 데이터가 수집되지 않는 경우

**증상**: `?? GPU 성능 카운터를 사용할 수 없습니다.`

**해결**:
1. 관리자 권한으로 실행
2. 그래픽 드라이버 최신 버전 확인
3. Windows 10/11에서만 지원됨

### 그래프 생성 실패

**증상**: Python 오류 또는 그래프 파일이 생성되지 않음

**해결**:
```powershell
# Python 패키지 재설치
pip install --upgrade matplotlib pandas

# 한글 폰트 문제시
pip install matplotlib --force-reinstall
```

### CSV 파일은 있는데 그래프가 안 생기는 경우

```powershell
# 수동으로 그래프 생성
python plot_performance.py performance_comparison_timeseries.csv

# 오류 메시지 확인
python plot_performance.py performance_comparison_timeseries.csv 2>&1
```

## 고급 활용

### 여러 번 테스트 실행하여 평균 비교

```powershell
# 3번 실행하여 결과 수집
for ($i=1; $i -le 3; $i++) {
    Write-Host "Test Run $i"
    .\RunFullTest.ps1 -NoGraph
    Rename-Item "performance_comparison_timeseries.csv" "run${i}_comparison.csv"
}

# 각 결과를 개별 분석
```

### CSV를 Excel로 분석

1. Excel 열기
2. 데이터 > 텍스트/CSV 가져오기
3. CSV 파일 선택
4. 피벗 테이블, 차트 생성

### 장기간 모니터링

측정 시간을 늘려서 장시간 안정성 테스트:

```csharp
// BorderServiceAdvancedPerformanceTests.cs 수정
private const int MeasurementDurationMs = 60000; // 1분
private const int SamplingIntervalMs = 500;      // 0.5초
```

## 파일 구조

```
프로젝트 루트/
├── CustomWindow/
│   ├── CustomWindow.Tests/
│   │   ├── BorderServicePerformanceTests.cs           # 기본 테스트
│   │   ├── BorderServiceAdvancedPerformanceTests.cs   # 고급 테스트
│   │   └── PerformanceResultExporter.cs              # CSV 내보내기
│   └── BorderService_test_winrt2/
├── RunPerformanceTests.ps1        # 대화형 테스트 실행
├── QuickTest.ps1                  # 빠른 테스트
├── RunFullTest.ps1                # 통합 실행 (테스트+그래프)
├── GenerateGraphs.ps1             # 그래프만 생성
├── plot_performance.py            # Python 그래프 스크립트
├── PERFORMANCE_TEST_GUIDE.md      # 종합 가이드
└── GRAPH_GENERATION_GUIDE.md      # 그래프 생성 가이드
```

## 추가 문서

- ?? [그래프 생성 상세 가이드](GRAPH_GENERATION_GUIDE.md)
- ?? [테스트 README](CustomWindow/CustomWindow.Tests/BorderServicePerformanceTests.README.md)

## 라이선스 및 기여

이 테스트 도구는 CustomWindow 프로젝트의 일부입니다.

## 연락처

문제가 있거나 개선 제안이 있으면 GitHub Issues에 등록해주세요.

---

**마지막 업데이트**: 2024
**버전**: 2.0 (그래프 생성 기능 추가)
