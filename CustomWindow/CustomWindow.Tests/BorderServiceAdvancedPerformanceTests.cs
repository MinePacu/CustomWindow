using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace CustomWindow.Tests;

/// <summary>
/// GPU 사용률을 포함한 고급 성능 비교 테스트
/// Performance Counter를 사용하여 GPU 메트릭을 측정합니다.
/// </summary>
public class BorderServiceAdvancedPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<Process> _processesToCleanup = new();

    private const string BorderServiceTestWinrtPath = @"C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt";
    private const string BorderServiceTestWinrt2Path = @"CustomWindow\BorderService_test_winrt2";

    private const int WarmupDurationMs = 3000;
    private const int MeasurementDurationMs = 15000;
    private const int SamplingIntervalMs = 200;

    public BorderServiceAdvancedPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        CleanupProcesses();
    }

    [Fact(DisplayName = "고급 성능 비교: GPU 사용률 포함")]
    [Trait("Category", "AdvancedPerformance")]
    public void CompareAdvancedPerformance_WithGPU()
    {
        _output.WriteLine("=== 고급 창 모서리 렌더링 성능 비교 (GPU 포함) ===\n");

        // GPU 성능 카운터 초기화
        var gpuCounters = InitializeGPUCounters();
        if (gpuCounters == null || gpuCounters.Count == 0)
        {
            _output.WriteLine("?? GPU 성능 카운터를 사용할 수 없습니다. CPU/메모리만 측정합니다.");
        }
        else
        {
            _output.WriteLine($"? GPU 성능 카운터 초기화 완료 ({gpuCounters.Count}개 어댑터)\n");
        }

        // 1. BorderService_Test_winrt 측정
        _output.WriteLine("--- BorderService_Test_winrt (개별 창 방식) ---");
        var metrics1 = MeasureBorderServiceTestWinrt(gpuCounters);
        
        if (metrics1 != null)
        {
            PrintAdvancedMetrics("BorderService_Test_winrt", metrics1);
            
            // 시계열 데이터 저장
            if (metrics1.TimeSeriesData != null && metrics1.TimeSeriesData.Count > 0)
            {
                var csvPath1 = "performance_test_winrt_timeseries.csv";
                PerformanceResultExporter.ExportSingleTimeSeriesData(csvPath1, metrics1.TimeSeriesData, "Test_winrt");
                _output.WriteLine($"? 시계열 데이터 저장됨: {csvPath1}\n");
            }
        }
        else
        {
            _output.WriteLine("?? BorderService_Test_winrt를 실행할 수 없습니다.\n");
        }

        CleanupProcesses();
        Thread.Sleep(3000);

        // 2. BorderService_test_winrt2 측정
        _output.WriteLine("\n--- BorderService_test_winrt2 (DirectComposition 방식) ---");
        var metrics2 = MeasureBorderServiceTestWinrt2(gpuCounters);
        
        if (metrics2 != null)
        {
            PrintAdvancedMetrics("BorderService_test_winrt2", metrics2);
            
            // 시계열 데이터 저장
            if (metrics2.TimeSeriesData != null && metrics2.TimeSeriesData.Count > 0)
            {
                var csvPath2 = "performance_test_winrt2_timeseries.csv";
                PerformanceResultExporter.ExportSingleTimeSeriesData(csvPath2, metrics2.TimeSeriesData, "test_winrt2");
                _output.WriteLine($"? 시계열 데이터 저장됨: {csvPath2}\n");
            }
        }
        else
        {
            _output.WriteLine("?? BorderService_test_winrt2를 실행할 수 없습니다.\n");
        }

        CleanupProcesses();

        // 3. 결과 비교 및 통합 CSV 생성
        if (metrics1 != null && metrics2 != null)
        {
            _output.WriteLine("\n" + new string('=', 75));
            _output.WriteLine("                         성능 비교 결과");
            _output.WriteLine(new string('=', 75) + "\n");
            CompareAdvancedResults(metrics1, metrics2);

            // 통합 시계열 데이터 CSV 생성
            if (metrics1.TimeSeriesData != null && metrics2.TimeSeriesData != null)
            {
                var csvPathCombined = "performance_comparison_timeseries.csv";
                PerformanceResultExporter.ExportTimeSeriesData(
                    csvPathCombined,
                    metrics1.TimeSeriesData,
                    metrics2.TimeSeriesData,
                    "Test_winrt",
                    "test_winrt2");
                _output.WriteLine($"\n? 통합 시계열 데이터 저장됨: {csvPathCombined}");
                _output.WriteLine($"  → 그래프 생성: python plot_performance.py {csvPathCombined}\n");
            }
        }

        // GPU 카운터 정리
        if (gpuCounters != null)
        {
            foreach (var counter in gpuCounters)
            {
                counter?.Dispose();
            }
        }
    }

    [Fact(DisplayName = "부하 테스트: 다중 창 시나리오")]
    [Trait("Category", "LoadTest")]
    public void LoadTest_MultipleWindows()
    {
        _output.WriteLine("=== 다중 창 시나리오 부하 테스트 ===\n");
        _output.WriteLine("여러 개의 notepad 창을 열어 부하를 시뮬레이션합니다.\n");

        // notepad 창 여러 개 실행
        var notepadProcesses = new List<Process>();
        const int windowCount = 5;

        try
        {
            _output.WriteLine($"테스트 창 {windowCount}개 실행 중...");
            for (int i = 0; i < windowCount; i++)
            {
                var notepad = Process.Start("notepad.exe");
                notepadProcesses.Add(notepad);
                Thread.Sleep(300);
            }

            _output.WriteLine($"? {windowCount}개 창 실행 완료\n");
            Thread.Sleep(2000);

            // 두 방식 비교
            _output.WriteLine("--- BorderService_Test_winrt 부하 테스트 ---");
            var metrics1 = MeasureBorderServiceTestWinrt(null);
            if (metrics1 != null)
            {
                _output.WriteLine($"평균 CPU: {metrics1.AverageCpuPercent:F2}%, 평균 메모리: {metrics1.AverageMemoryMB:F2} MB");
            }

            CleanupProcesses();
            Thread.Sleep(2000);

            _output.WriteLine("\n--- BorderService_test_winrt2 부하 테스트 ---");
            var metrics2 = MeasureBorderServiceTestWinrt2(null);
            if (metrics2 != null)
            {
                _output.WriteLine($"평균 CPU: {metrics2.AverageCpuPercent:F2}%, 평균 메모리: {metrics2.AverageMemoryMB:F2} MB");
            }

            CleanupProcesses();

            if (metrics1 != null && metrics2 != null)
            {
                _output.WriteLine("\n부하 테스트 결과:");
                _output.WriteLine($"  CPU 효율: {(metrics1.AverageCpuPercent < metrics2.AverageCpuPercent ? "Test_winrt" : "test_winrt2")} 우수");
                _output.WriteLine($"  메모리 효율: {(metrics1.AverageMemoryMB < metrics2.AverageMemoryMB ? "Test_winrt" : "test_winrt2")} 우수");
            }
        }
        finally
        {
            // notepad 프로세스 정리
            foreach (var proc in notepadProcesses)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                        proc.WaitForExit(500);
                    }
                    proc.Dispose();
                }
                catch { }
            }
        }
    }

    private List<PerformanceCounter>? InitializeGPUCounters()
    {
        try
        {
            var counters = new List<PerformanceCounter>();
            
            // GPU Engine 카테고리가 있는지 확인
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return null;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();

            // 3D 엔진 카운터 찾기
            foreach (var instance in instanceNames)
            {
                if (instance.Contains("engtype_3D"))
                {
                    try
                    {
                        var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                        counters.Add(counter);
                    }
                    catch { }
                }
            }

            return counters.Count > 0 ? counters : null;
        }
        catch
        {
            return null;
        }
    }

    private AdvancedPerformanceMetrics? MeasureBorderServiceTestWinrt(List<PerformanceCounter>? gpuCounters)
    {
        string[] possiblePaths = {
            Path.Combine(BorderServiceTestWinrtPath, @"x64\Release\BorderService_Test_winrt.exe"),
            Path.Combine(BorderServiceTestWinrtPath, @"Release\BorderService_Test_winrt.exe"),
        };

        string? exePath = possiblePaths.FirstOrDefault(File.Exists);
        if (exePath == null) return null;

        return MeasureAdvancedPerformance(exePath, "BorderService_Test_winrt", gpuCounters);
    }

    private AdvancedPerformanceMetrics? MeasureBorderServiceTestWinrt2(List<PerformanceCounter>? gpuCounters)
    {
        string[] possiblePaths = {
            Path.Combine(BorderServiceTestWinrt2Path, @"x64\Release\BorderService_test_winrt2.exe"),
            Path.Combine(BorderServiceTestWinrt2Path, @"BorderService_test_winrt2\x64\Release\BorderService_test_winrt2.exe"),
        };

        string? exePath = possiblePaths.FirstOrDefault(File.Exists);
        if (exePath == null) return null;

        return MeasureAdvancedPerformance(exePath, "BorderService_test_winrt2", gpuCounters);
    }

    private AdvancedPerformanceMetrics MeasureAdvancedPerformance(
        string exePath, 
        string processName,
        List<PerformanceCounter>? gpuCounters)
    {
        _output.WriteLine($"실행: {Path.GetFileName(exePath)}");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Assert.NotNull(process);
        _processesToCleanup.Add(process);
        _output.WriteLine($"PID: {process.Id}");

        // 워밍업
        _output.WriteLine($"워밍업 {WarmupDurationMs}ms...");
        Thread.Sleep(WarmupDurationMs);

        // 측정 시작
        _output.WriteLine($"측정 시작 ({MeasurementDurationMs}ms)...");

        var metrics = new AdvancedPerformanceMetrics();
        var cpuSamples = new List<double>();
        var memorySamples = new List<long>();
        var workingSetSamples = new List<long>();
        var gpuSamples = new List<double>();
        var handleCountSamples = new List<int>();
        var threadCountSamples = new List<int>();
        
        // 시계열 데이터 수집
        var timeSeriesData = new List<PerformanceResultExporter.TimeSeriesSample>();

        var stopwatch = Stopwatch.StartNew();
        var prevCpuTime = process.TotalProcessorTime;
        var prevTime = DateTime.UtcNow;

        while (stopwatch.ElapsedMilliseconds < MeasurementDurationMs)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) break;

                // CPU 측정
                var currentTime = DateTime.UtcNow;
                var currentCpuTime = process.TotalProcessorTime;
                var cpuUsed = (currentCpuTime - prevCpuTime).TotalMilliseconds;
                var totalTime = (currentTime - prevTime).TotalMilliseconds;
                var cpuPercent = (cpuUsed / (Environment.ProcessorCount * totalTime)) * 100;
                
                if (cpuPercent >= 0 && cpuPercent <= 100)
                {
                    cpuSamples.Add(cpuPercent);
                }

                prevCpuTime = currentCpuTime;
                prevTime = currentTime;

                // 메모리 측정
                var memoryBytes = process.PrivateMemorySize64;
                var workingSetBytes = process.WorkingSet64;
                memorySamples.Add(memoryBytes);
                workingSetSamples.Add(workingSetBytes);

                // 핸들 및 스레드 수
                var handleCount = process.HandleCount;
                var threadCount = process.Threads.Count;
                handleCountSamples.Add(handleCount);
                threadCountSamples.Add(threadCount);

                // GPU 측정
                double gpuPercent = 0;
                if (gpuCounters != null && gpuCounters.Count > 0)
                {
                    float totalGpu = 0;
                    int validCounters = 0;
                    foreach (var counter in gpuCounters)
                    {
                        try
                        {
                            var value = counter.NextValue();
                            if (value >= 0)
                            {
                                totalGpu += value;
                                validCounters++;
                            }
                        }
                        catch { }
                    }
                    if (validCounters > 0)
                    {
                        gpuPercent = totalGpu / validCounters;
                        gpuSamples.Add(gpuPercent);
                    }
                }

                // 시계열 샘플 추가
                timeSeriesData.Add(new PerformanceResultExporter.TimeSeriesSample
                {
                    ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                    CpuPercent = cpuPercent >= 0 && cpuPercent <= 100 ? cpuPercent : 0,
                    MemoryMB = memoryBytes / (1024.0 * 1024.0),
                    WorkingSetMB = workingSetBytes / (1024.0 * 1024.0),
                    GpuPercent = gpuPercent,
                    HandleCount = handleCount,
                    ThreadCount = threadCount
                });

                Thread.Sleep(SamplingIntervalMs);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"측정 오류: {ex.Message}");
                break;
            }
        }

        stopwatch.Stop();

        // 통계 계산
        if (cpuSamples.Count > 0)
        {
            metrics.AverageCpuPercent = cpuSamples.Average();
            metrics.MaxCpuPercent = cpuSamples.Max();
            metrics.MinCpuPercent = cpuSamples.Min();
            metrics.StdDevCpuPercent = CalculateStdDev(cpuSamples);
        }

        if (memorySamples.Count > 0)
        {
            metrics.AverageMemoryMB = memorySamples.Average() / (1024.0 * 1024.0);
            metrics.MaxMemoryMB = memorySamples.Max() / (1024.0 * 1024.0);
            metrics.MinMemoryMB = memorySamples.Min() / (1024.0 * 1024.0);
        }

        if (workingSetSamples.Count > 0)
        {
            metrics.AverageWorkingSetMB = workingSetSamples.Average() / (1024.0 * 1024.0);
            metrics.MaxWorkingSetMB = workingSetSamples.Max() / (1024.0 * 1024.0);
        }

        if (gpuSamples.Count > 0)
        {
            metrics.AverageGpuPercent = gpuSamples.Average();
            metrics.MaxGpuPercent = gpuSamples.Max();
            metrics.HasGpuData = true;
        }

        if (handleCountSamples.Count > 0)
        {
            metrics.AverageHandleCount = handleCountSamples.Average();
            metrics.MaxHandleCount = handleCountSamples.Max();
        }

        if (threadCountSamples.Count > 0)
        {
            metrics.AverageThreadCount = threadCountSamples.Average();
        }

        metrics.MeasurementDurationMs = stopwatch.ElapsedMilliseconds;
        metrics.SampleCount = cpuSamples.Count;
        metrics.TimeSeriesData = timeSeriesData;

        _output.WriteLine($"? 측정 완료 ({metrics.SampleCount} 샘플)\n");

        return metrics;
    }

    private double CalculateStdDev(List<double> values)
    {
        if (values.Count == 0) return 0;
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumOfSquares / values.Count);
    }

    private void PrintAdvancedMetrics(string name, AdvancedPerformanceMetrics metrics)
    {
        _output.WriteLine($"?? {name} 상세 성능:");
        _output.WriteLine($"   측정 시간: {metrics.MeasurementDurationMs:N0}ms, 샘플: {metrics.SampleCount}");
        _output.WriteLine("");
        
        _output.WriteLine($"   ?? CPU:");
        _output.WriteLine($"      평균: {metrics.AverageCpuPercent:F2}% (±{metrics.StdDevCpuPercent:F2}%)");
        _output.WriteLine($"      범위: {metrics.MinCpuPercent:F2}% ~ {metrics.MaxCpuPercent:F2}%");
        _output.WriteLine("");
        
        _output.WriteLine($"   ?? 메모리:");
        _output.WriteLine($"      Private: {metrics.AverageMemoryMB:F2} MB (최대 {metrics.MaxMemoryMB:F2} MB)");
        _output.WriteLine($"      WorkingSet: {metrics.AverageWorkingSetMB:F2} MB (최대 {metrics.MaxWorkingSetMB:F2} MB)");
        _output.WriteLine("");
        
        if (metrics.HasGpuData)
        {
            _output.WriteLine($"   ?? GPU:");
            _output.WriteLine($"      평균: {metrics.AverageGpuPercent:F2}%");
            _output.WriteLine($"      최대: {metrics.MaxGpuPercent:F2}%");
            _output.WriteLine("");
        }
        
        _output.WriteLine($"   ?? 리소스:");
        _output.WriteLine($"      핸들: {metrics.AverageHandleCount:F0} (최대 {metrics.MaxHandleCount})");
        _output.WriteLine($"      스레드: {metrics.AverageThreadCount:F1}");
        _output.WriteLine("");
    }

    private void CompareAdvancedResults(AdvancedPerformanceMetrics m1, AdvancedPerformanceMetrics m2)
    {
        // CPU 비교
        _output.WriteLine("?? CPU 사용률:");
        PrintComparison("   평균", m1.AverageCpuPercent, m2.AverageCpuPercent, "%");
        PrintComparison("   최대", m1.MaxCpuPercent, m2.MaxCpuPercent, "%");
        PrintComparison("   안정성 (표준편차)", m1.StdDevCpuPercent, m2.StdDevCpuPercent, "%");
        _output.WriteLine("");

        // 메모리 비교
        _output.WriteLine("?? 메모리 사용량:");
        PrintComparison("   평균 Private", m1.AverageMemoryMB, m2.AverageMemoryMB, "MB");
        PrintComparison("   최대 Private", m1.MaxMemoryMB, m2.MaxMemoryMB, "MB");
        PrintComparison("   평균 WorkingSet", m1.AverageWorkingSetMB, m2.AverageWorkingSetMB, "MB");
        _output.WriteLine("");

        // GPU 비교
        if (m1.HasGpuData && m2.HasGpuData)
        {
            _output.WriteLine("?? GPU 사용률:");
            PrintComparison("   평균", m1.AverageGpuPercent, m2.AverageGpuPercent, "%");
            PrintComparison("   최대", m1.MaxGpuPercent, m2.MaxGpuPercent, "%");
            _output.WriteLine("");
        }

        // 리소스 비교
        _output.WriteLine("?? 시스템 리소스:");
        PrintComparison("   평균 핸들 수", m1.AverageHandleCount, m2.AverageHandleCount, "");
        PrintComparison("   최대 핸들 수", m1.MaxHandleCount, m2.MaxHandleCount, "");
        PrintComparison("   평균 스레드 수", m1.AverageThreadCount, m2.AverageThreadCount, "");
        _output.WriteLine("");

        // 종합 평가
        int score1 = 0, score2 = 0;
        if (m1.AverageCpuPercent < m2.AverageCpuPercent) score1++; else score2++;
        if (m1.AverageMemoryMB < m2.AverageMemoryMB) score1++; else score2++;
        if (m1.StdDevCpuPercent < m2.StdDevCpuPercent) score1++; else score2++;
        
        if (m1.HasGpuData && m2.HasGpuData)
        {
            if (m1.AverageGpuPercent < m2.AverageGpuPercent) score1++; else score2++;
        }

        _output.WriteLine(new string('-', 75));
        _output.WriteLine("?? 종합 평가:");
        _output.WriteLine($"   BorderService_Test_winrt:   {score1}점");
        _output.WriteLine($"   BorderService_test_winrt2:  {score2}점");
        _output.WriteLine("");

        if (score1 > score2)
            _output.WriteLine("   ? 개별 창 방식(Test_winrt)이 더 효율적입니다.");
        else if (score2 > score1)
            _output.WriteLine("   ? DirectComposition 방식(test_winrt2)이 더 효율적입니다.");
        else
            _output.WriteLine("   ??  두 방식의 성능이 비슷합니다.");

        _output.WriteLine(new string('-', 75));
    }

    private void PrintComparison(string label, double v1, double v2, string unit)
    {
        double diff = v2 - v1;
        double diffPercent = v1 != 0 ? (diff / v1) * 100 : 0;
        
        string symbol = diffPercent > 5 ? "▲" : diffPercent < -5 ? "▼" : "?";
        string winner = Math.Abs(diffPercent) < 5 ? "동등" :
                       v1 < v2 ? "Test_winrt 우수" : "test_winrt2 우수";

        _output.WriteLine($"{label}:");
        _output.WriteLine($"      Test_winrt: {v1:F2} {unit}");
        _output.WriteLine($"      test_winrt2: {v2:F2} {unit}");
        _output.WriteLine($"      차이: {symbol} {Math.Abs(diffPercent):F1}% ({winner})");
    }

    private void CleanupProcesses()
    {
        foreach (var proc in _processesToCleanup)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                }
                proc.Dispose();
            }
            catch { }
        }
        _processesToCleanup.Clear();
    }

    private class AdvancedPerformanceMetrics
    {
        public double AverageCpuPercent { get; set; }
        public double MaxCpuPercent { get; set; }
        public double MinCpuPercent { get; set; }
        public double StdDevCpuPercent { get; set; }
        
        public double AverageMemoryMB { get; set; }
        public double MaxMemoryMB { get; set; }
        public double MinMemoryMB { get; set; }
        
        public double AverageWorkingSetMB { get; set; }
        public double MaxWorkingSetMB { get; set; }
        
        public double AverageGpuPercent { get; set; }
        public double MaxGpuPercent { get; set; }
        public bool HasGpuData { get; set; }
        
        public double AverageHandleCount { get; set; }
        public int MaxHandleCount { get; set; }
        public double AverageThreadCount { get; set; }
        
        public long MeasurementDurationMs { get; set; }
        public int SampleCount { get; set; }

        // 시계열 데이터
        public List<PerformanceResultExporter.TimeSeriesSample>? TimeSeriesData { get; set; }
    }
}
