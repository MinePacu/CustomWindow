# ?? 빠른 시작 가이드

BorderService 성능 테스트 및 그래프 생성을 3단계로 빠르게 시작하세요!

## ?? 3단계 시작

### 1?? 프로젝트 빌드

```powershell
# 두 프로젝트를 Release/x64로 빌드
# Visual Studio에서 또는:
msbuild "C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt.sln" /p:Configuration=Release /p:Platform=x64
msbuild "CustomWindow\BorderService_test_winrt2\BorderService_test_winrt2.sln" /p:Configuration=Release /p:Platform=x64
```

### 2?? 원클릭 실행

```powershell
# 테스트 + CSV 생성 + 그래프 생성 자동 실행!
.\RunFullTest.ps1
```

### 3?? 결과 확인

생성된 그래프 파일을 열어서 성능 비교를 확인하세요:
- `performance_comparison_timeseries_graph.png` - 시간별 비교
- `performance_comparison_timeseries_summary.png` - 평균 요약

## ?? 실행 옵션

```powershell
# 기본 실행 (모든 것 자동)
.\RunFullTest.ps1

# 그래프 자동 열기
.\RunFullTest.ps1 -AutoOpen

# 기존 CSV로 그래프만 재생성
.\RunFullTest.ps1 -SkipTest

# 테스트만 실행 (그래프 생성 안 함)
.\RunFullTest.ps1 -NoGraph
```

## ?? 생성되는 파일

```
? CSV 데이터
   ? performance_test_winrt_timeseries.csv
   ? performance_test_winrt2_timeseries.csv
   ? performance_comparison_timeseries.csv

? 그래프 이미지
   ? performance_comparison_timeseries_graph.png
   ? performance_comparison_timeseries_summary.png
   ? performance_test_winrt_timeseries_graph.png
   ? performance_test_winrt2_timeseries_graph.png
```

## ?? 필요한 환경

- ? .NET 8 SDK
- ? Python 3.x (그래프 생성용)
- ? Visual Studio 2022 (C++ 프로젝트 빌드용)

Python 패키지는 자동으로 설치됩니다 (matplotlib, pandas).

## ? 문제 발생 시

```powershell
# 실행 파일을 찾을 수 없다면
# → Visual Studio에서 Release/x64로 빌드했는지 확인

# 그래프가 생성되지 않는다면
pip install matplotlib pandas

# 자세한 도움말
Get-Help .\RunFullTest.ps1 -Detailed
```

## ?? 자세한 문서

- [종합 가이드](PERFORMANCE_TEST_GUIDE.md)
- [그래프 생성 가이드](GRAPH_GENERATION_GUIDE.md)

## ?? 팁

- 테스트는 약 20-30초 소요됩니다
- 다른 프로그램을 종료하면 더 정확한 측정이 가능합니다
- 여러 번 실행하여 평균을 내면 더 신뢰할 수 있습니다

---

**문제가 있나요?** GitHub Issues에 올려주세요!
