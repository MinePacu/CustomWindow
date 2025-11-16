# 성능 그래프 생성 가이드

## ?? 개요

BorderService 성능 비교 테스트 결과를 시각적으로 확인할 수 있는 그래프 생성 도구입니다.

## ?? 생성되는 그래프

### 1. 비교 그래프 (Comparison Charts)
두 렌더링 방식을 동시에 비교하는 그래프:
- **CPU 사용률 비교**: 시간에 따른 CPU 사용량 변화
- **메모리 사용량 비교**: 시간에 따른 메모리 사용량 변화
- **GPU 사용률 비교**: 시간에 따른 GPU 사용량 변화 (데이터가 있는 경우)

### 2. 요약 차트 (Summary Charts)
평균값을 막대 그래프로 비교:
- 평균 CPU 사용률
- 평균 메모리 사용량
- 평균 GPU 사용률

### 3. 상세 모니터링 그래프 (Detailed Monitoring)
단일 프로세스의 모든 메트릭:
- CPU 사용률
- Private Memory
- Working Set
- GPU 사용률
- 핸들 수
- 스레드 수

## ?? 빠른 시작

### 1단계: Python 환경 설정

```powershell
# 자동 설정 스크립트 실행 (권장)
.\GenerateGraphs.ps1
```

또는 수동 설정:

```powershell
# Python 설치 확인
python --version

# 필요한 패키지 설치
pip install matplotlib pandas
```

### 2단계: 성능 테스트 실행

```powershell
# 고급 성능 테스트 실행 (CSV 파일 생성)
.\RunPerformanceTests.ps1

# 또는 직접 실행
dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
    --filter "DisplayName~고급 성능 비교"
```

### 3단계: 그래프 생성

```powershell
# 자동 생성 (대화형)
.\GenerateGraphs.ps1

# 또는 수동 생성
python plot_performance.py performance_comparison_timeseries.csv
python plot_performance.py performance_test_winrt_timeseries.csv
python plot_performance.py performance_test_winrt2_timeseries.csv
```

## ?? 생성되는 파일

테스트 실행 후 다음 파일들이 생성됩니다:

```
프로젝트 루트/
├── performance_test_winrt_timeseries.csv           # Test_winrt 시계열 데이터
├── performance_test_winrt_timeseries_graph.png     # Test_winrt 그래프
├── performance_test_winrt2_timeseries.csv          # test_winrt2 시계열 데이터
├── performance_test_winrt2_timeseries_graph.png    # test_winrt2 그래프
├── performance_comparison_timeseries.csv           # 통합 비교 데이터
├── performance_comparison_timeseries_graph.png     # 비교 그래프
└── performance_comparison_timeseries_summary.png   # 요약 차트
```

## ?? CSV 파일 형식

### 비교 데이터 (Comparison)

```csv
Time_Sec,Test_winrt_CPU%,Test_winrt_Memory_MB,Test_winrt_GPU%,test_winrt2_CPU%,test_winrt2_Memory_MB,test_winrt2_GPU%
0.00,2.45,42.30,1.20,1.85,38.50,0.95
0.20,2.52,42.45,1.25,1.90,38.52,1.00
0.40,2.48,42.50,1.22,1.88,38.55,0.98
...
```

### 단일 프로세스 데이터

```csv
Time_Sec,CPU%,Memory_MB,WorkingSet_MB,GPU%,Handle_Count,Thread_Count
0.00,2.45,42.30,50.20,1.20,245,12
0.20,2.52,42.45,50.35,1.25,247,12
0.40,2.48,42.50,50.40,1.22,246,12
...
```

## ?? 그래프 사용자 정의

### 스타일 변경

`plot_performance.py` 파일을 수정하여 스타일 변경 가능:

```python
# 스타일 옵션
plt.style.use('seaborn-v0_8-darkgrid')  # 기본
# plt.style.use('ggplot')                # ggplot 스타일
# plt.style.use('bmh')                   # Bayesian Methods for Hackers
# plt.style.use('classic')               # 클래식
```

### 색상 변경

```python
# 색상 코드 변경
color_test_winrt = '#2E86AB'    # 파란색
color_test_winrt2 = '#A23B72'   # 보라색

# 다른 색상 예시
# color_test_winrt = '#FF6B6B'  # 빨간색
# color_test_winrt2 = '#4ECDC4' # 청록색
```

### 해상도 변경

```python
# DPI 변경 (기본: 300)
plt.savefig(output_file, dpi=300, bbox_inches='tight')

# 고해상도: dpi=600
# 일반: dpi=150
```

## ?? 고급 사용법

### 특정 시간 범위만 표시

```python
# plot_performance.py 수정
df_filtered = df[(df['Time_Sec'] >= 2) & (df['Time_Sec'] <= 10)]
ax.plot(df_filtered['Time_Sec'], df_filtered['Test_winrt_CPU%'], ...)
```

### 이동 평균 추가

```python
# 5초 이동 평균
df['CPU_MA'] = df['Test_winrt_CPU%'].rolling(window=25).mean()  # 25 샘플 = 5초
ax.plot(df['Time_Sec'], df['CPU_MA'], label='Moving Average', linestyle='--')
```

### 여러 테스트 결과 비교

```python
# 여러 CSV 파일 읽어서 비교
df1 = pd.read_csv('test1_timeseries.csv')
df2 = pd.read_csv('test2_timeseries.csv')
df3 = pd.read_csv('test3_timeseries.csv')

ax.plot(df1['Time_Sec'], df1['Test_winrt_CPU%'], label='Run 1')
ax.plot(df2['Time_Sec'], df2['Test_winrt_CPU%'], label='Run 2')
ax.plot(df3['Time_Sec'], df3['Test_winrt_CPU%'], label='Run 3')
```

## ?? 그래프 예시

### CPU 사용률 비교
![CPU Usage](docs/cpu_usage_example.png)
- X축: 시간 (초)
- Y축: CPU 사용률 (%)
- 파란선: Test_winrt (개별 창 방식)
- 보라선: test_winrt2 (DirectComposition)

### 메모리 사용량 비교
![Memory Usage](docs/memory_usage_example.png)
- X축: 시간 (초)
- Y축: 메모리 사용량 (MB)
- 낮을수록 효율적

### 요약 차트
![Summary](docs/summary_example.png)
- 평균값을 막대 그래프로 표시
- 한눈에 우수한 방식 확인 가능

## ?? Python 스크립트 옵션

### 명령줄 인수

```powershell
# 기본 사용
python plot_performance.py <csv_file>

# 비교 그래프 생성
python plot_performance.py performance_comparison_timeseries.csv

# 단일 프로세스 그래프 생성
python plot_performance.py performance_test_winrt_timeseries.csv
```

### 스크립트 내부 수정

```python
# 그래프 크기 조정
fig, axes = plt.subplots(3, 1, figsize=(14, 10))  # 기본
# figsize=(20, 15) - 더 큰 그래프
# figsize=(10, 8)  - 더 작은 그래프

# 선 두께 조정
linewidth=2  # 기본
# linewidth=3 - 더 굵게
# linewidth=1 - 더 얇게

# 마커 크기 조정
markersize=3  # 기본
# markersize=5 - 더 크게
# markersize=0 - 마커 제거
```

## ?? 문제 해결

### Python을 찾을 수 없는 경우

```powershell
# Python 설치 확인
where.exe python

# PATH에 추가되어 있지 않으면
$env:PATH += ";C:\Python3X"  # Python 설치 경로로 변경
```

### matplotlib 설치 오류

```powershell
# 관리자 권한으로 실행
pip install --upgrade pip
pip install matplotlib pandas

# 또는 사용자 디렉터리에 설치
pip install --user matplotlib pandas
```

### 한글 깨짐 문제

```python
# plot_performance.py에 추가
import matplotlib.font_manager as fm

# 한글 폰트 설정
plt.rcParams['font.family'] = 'Malgun Gothic'  # 맑은 고딕
plt.rcParams['axes.unicode_minus'] = False     # 마이너스 기호 표시
```

### CSV 파일을 찾을 수 없는 경우

```powershell
# 현재 디렉터리 확인
Get-Location

# CSV 파일 검색
Get-ChildItem -Recurse -Filter "*timeseries.csv"

# 테스트 재실행
dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
    --filter "DisplayName~고급 성능 비교"
```

### 그래프가 표시되지 않는 경우

```python
# 백엔드 변경 (plot_performance.py 상단에 추가)
import matplotlib
matplotlib.use('TkAgg')  # 또는 'Qt5Agg', 'WXAgg'
```

## ?? 추가 분석

### Excel에서 열기

CSV 파일을 Excel에서 직접 열어 분석 가능:
1. Excel 실행
2. 파일 > 열기 > CSV 파일 선택
3. 피벗 테이블, 차트 생성 가능

### Jupyter Notebook 사용

더 자세한 분석을 위해:

```python
import pandas as pd
import matplotlib.pyplot as plt

# 데이터 로드
df = pd.read_csv('performance_comparison_timeseries.csv')

# 기술 통계
print(df.describe())

# 상관관계 분석
print(df.corr())

# 사용자 정의 그래프
plt.figure(figsize=(12, 6))
plt.plot(df['Time_Sec'], df['Test_winrt_CPU%'])
plt.show()
```

## ?? 참고 자료

- [Matplotlib 공식 문서](https://matplotlib.org/stable/contents.html)
- [Pandas 공식 문서](https://pandas.pydata.org/docs/)
- [Python 그래프 튜토리얼](https://realpython.com/python-matplotlib-guide/)

## ?? 라이선스

이 도구는 CustomWindow 프로젝트의 일부입니다.

---

**마지막 업데이트**: 2024
**작성자**: BorderService Performance Testing Team
