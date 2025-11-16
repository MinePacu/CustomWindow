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
/// 두 가지 창 모서리 렌더링 방식의 성능을 비교하는 테스트
/// 1. BorderService_Test_winrt: 개별 창별 D2D HwndRenderTarget 방식
/// 2. BorderService_test_winrt2: DirectComposition 전체 화면 오버레이 방식
/// </summary>
public class BorderServicePerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<Process> _processesToCleanup = new();

    // 테스트 대상 실행 파일 경로
    private const string BorderServiceTestWinrtPath = @"C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt";
    private const string BorderServiceTestWinrt2Path = @"CustomWindow\BorderService_test_winrt2";

    // 성능 측정 관련 상수
    private const int WarmupDurationMs = 2000;      // 워밍업 시간
    private const int MeasurementDurationMs = 10000; // 측정 시간 (10초)
    private const int SamplingIntervalMs = 100;      // 샘플링 간격

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryProcessCycleTime(IntPtr hProcess, out ulong cycleTime);

    public BorderServicePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        // 테스트 후 프로세스 정리
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

    [Fact(DisplayName = "성능 비교: BorderService_Test_winrt vs BorderService_test_winrt2")]
    [Trait("Category", "Performance")]
    public void ComparePerformance_BothRenderingMethods()
    {
        _output.WriteLine("=== 창 모서리 렌더링 방식 성능 비교 테스트 ===\n");

        // 1. BorderService_Test_winrt 테스트 (개별 창 방식)
        _output.WriteLine("--- BorderService_Test_winrt (개별 창 D2D HwndRenderTarget) ---");
        var metrics1 = MeasureBorderServiceTestWinrt();
        
        if (metrics1 != null)
        {
            PrintMetrics("BorderService_Test_winrt", metrics1);
        }
        else
        {
            _output.WriteLine("⚠️ BorderService_Test_winrt 실행 파일을 찾을 수 없습니다.");
            _output.WriteLine($"   경로: {BorderServiceTestWinrtPath}");
        }

        // 프로세스 정리 및 대기
        CleanupProcesses();
        Thread.Sleep(2000);

        // 2. BorderService_test_winrt2 테스트 (DirectComposition 오버레이)
        _output.WriteLine("\n--- BorderService_test_winrt2 (DirectComposition 전체 화면 오버레이) ---");
        var metrics2 = MeasureBorderServiceTestWinrt2();
        
        if (metrics2 != null)
        {
            PrintMetrics("BorderService_test_winrt2", metrics2);
        }
        else
        {
            _output.WriteLine("⚠️ BorderService_test_winrt2 실행 파일을 찾을 수 없습니다.");
            _output.WriteLine($"   경로: {BorderServiceTestWinrt2Path}");
        }

        // 프로세스 정리
        CleanupProcesses();

        // 3. 결과 비교
        if (metrics1 != null && metrics2 != null)
        {
            _output.WriteLine("\n=== 성능 비교 결과 ===");
            CompareAndPrintResults(metrics1, metrics2);
        }
        else
        {
            _output.WriteLine("\n⚠️ 두 프로그램 중 하나 이상을 실행할 수 없어 비교를 건너뜁니다.");
        }
    }

    private PerformanceMetrics? MeasureBorderServiceTestWinrt()
    {
        // Release 빌드 경로 찾기
        string[] possiblePaths = {
            Path.Combine(BorderServiceTestWinrtPath, @"x64\Release\BorderService_Test_winrt.exe"),
            Path.Combine(BorderServiceTestWinrtPath, @"Release\BorderService_Test_winrt.exe"),
            Path.Combine(BorderServiceTestWinrtPath, @"BorderService_Test_winrt.exe")
        };

        string? exePath = possiblePaths.FirstOrDefault(File.Exists);
        if (exePath == null)
        {
            return null;
        }

        return MeasurePerformance(exePath, "BorderService_Test_winrt");
    }

    private PerformanceMetrics? MeasureBorderServiceTestWinrt2()
    {
        // Release 빌드 경로 찾기
        string[] possiblePaths = {
            Path.Combine(BorderServiceTestWinrt2Path, @"x64\Release\BorderService_test_winrt2.exe"),
            Path.Combine(BorderServiceTestWinrt2Path, @"Release\BorderService_test_winrt2.exe"),
            Path.Combine(BorderServiceTestWinrt2Path, @"BorderService_test_winrt2\x64\Release\BorderService_test_winrt2.exe"),
            Path.Combine(BorderServiceTestWinrt2Path, @"BorderService_test_winrt2\Release\BorderService_test_winrt2.exe")
        };

        string? exePath = possiblePaths.FirstOrDefault(File.Exists);
        if (exePath == null)
        {
            return null;
        }

        return MeasurePerformance(exePath, "BorderService_test_winrt2");
    }

    private PerformanceMetrics MeasurePerformance(string exePath, string processName)
    {
        _output.WriteLine($"실행 파일: {exePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        _processesToCleanup.Add(process);

        _output.WriteLine($"프로세스 시작됨 (PID: {process.Id})");

        // 워밍업 시간
        _output.WriteLine($"워밍업 중... ({WarmupDurationMs}ms)");
        Thread.Sleep(WarmupDurationMs);

        // 성능 측정 시작
        _output.WriteLine($"성능 측정 시작 ({MeasurementDurationMs}ms)...");
        
        var metrics = new PerformanceMetrics();
        var cpuSamples = new List<double>();
        var memorySamples = new List<long>();
        var workingSetSamples = new List<long>();
        
        var stopwatch = Stopwatch.StartNew();
        ulong initialCycleTime = 0;
        QueryProcessCycleTime(process.Handle, out initialCycleTime);

        while (stopwatch.ElapsedMilliseconds < MeasurementDurationMs)
        {
            try
            {
                process.Refresh();

                if (process.HasExited)
                {
                    _output.WriteLine("⚠️ 프로세스가 예기치 않게 종료되었습니다.");
                    break;
                }

                // CPU 사용률 샘플링
                var cpuUsage = GetProcessCpuUsage(process);
                if (cpuUsage >= 0)
                {
                    cpuSamples.Add(cpuUsage);
                }

                // 메모리 사용량 샘플링
                memorySamples.Add(process.PrivateMemorySize64);
                workingSetSamples.Add(process.WorkingSet64);

                Thread.Sleep(SamplingIntervalMs);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"측정 중 예외 발생: {ex.Message}");
                break;
            }
        }

        stopwatch.Stop();

        // 최종 CPU 사이클 시간
        ulong finalCycleTime = 0;
        QueryProcessCycleTime(process.Handle, out finalCycleTime);

        // 통계 계산
        if (cpuSamples.Count > 0)
        {
            metrics.AverageCpuPercent = cpuSamples.Average();
            metrics.MaxCpuPercent = cpuSamples.Max();
            metrics.MinCpuPercent = cpuSamples.Min();
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

        metrics.TotalCpuCycles = finalCycleTime - initialCycleTime;
        metrics.MeasurementDurationMs = stopwatch.ElapsedMilliseconds;
        metrics.SampleCount = cpuSamples.Count;

        _output.WriteLine($"측정 완료 (샘플 수: {metrics.SampleCount})");

        return metrics;
    }

    private double GetProcessCpuUsage(Process process)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100);

            process.Refresh();
            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return cpuUsageTotal * 100;
        }
        catch
        {
            return -1;
        }
    }

    private void PrintMetrics(string name, PerformanceMetrics metrics)
    {
        _output.WriteLine($"\n📊 {name} 성능 측정 결과:");
        _output.WriteLine($"   측정 시간: {metrics.MeasurementDurationMs:N0}ms");
        _output.WriteLine($"   샘플 수: {metrics.SampleCount}");
        _output.WriteLine($"");
        _output.WriteLine($"   CPU 사용률:");
        _output.WriteLine($"      평균: {metrics.AverageCpuPercent:F2}%");
        _output.WriteLine($"      최대: {metrics.MaxCpuPercent:F2}%");
        _output.WriteLine($"      최소: {metrics.MinCpuPercent:F2}%");
        _output.WriteLine($"");
        _output.WriteLine($"   메모리 사용량 (Private):");
        _output.WriteLine($"      평균: {metrics.AverageMemoryMB:F2} MB");
        _output.WriteLine($"      최대: {metrics.MaxMemoryMB:F2} MB");
        _output.WriteLine($"      최소: {metrics.MinMemoryMB:F2} MB");
        _output.WriteLine($"");
        _output.WriteLine($"   Working Set:");
        _output.WriteLine($"      평균: {metrics.AverageWorkingSetMB:F2} MB");
        _output.WriteLine($"      최대: {metrics.MaxWorkingSetMB:F2} MB");
        _output.WriteLine($"");
        _output.WriteLine($"   총 CPU 사이클: {metrics.TotalCpuCycles:N0}");
    }

    private void CompareAndPrintResults(PerformanceMetrics metrics1, PerformanceMetrics metrics2)
    {
        _output.WriteLine("");
        _output.WriteLine("┌─────────────────────────────────────────────────────────────────────┐");
        _output.WriteLine("│                        성능 비교 요약                                │");
        _output.WriteLine("└─────────────────────────────────────────────────────────────────────┘");
        _output.WriteLine("");

        PrintComparison("평균 CPU 사용률", 
            metrics1.AverageCpuPercent, 
            metrics2.AverageCpuPercent, 
            "%", 
            lowerIsBetter: true);

        PrintComparison("최대 CPU 사용률", 
            metrics1.MaxCpuPercent, 
            metrics2.MaxCpuPercent, 
            "%", 
            lowerIsBetter: true);

        PrintComparison("평균 메모리 사용량", 
            metrics1.AverageMemoryMB, 
            metrics2.AverageMemoryMB, 
            "MB", 
            lowerIsBetter: true);

        PrintComparison("최대 메모리 사용량", 
            metrics1.MaxMemoryMB, 
            metrics2.MaxMemoryMB, 
            "MB", 
            lowerIsBetter: true);

        PrintComparison("평균 Working Set", 
            metrics1.AverageWorkingSetMB, 
            metrics2.AverageWorkingSetMB, 
            "MB", 
            lowerIsBetter: true);

        PrintComparison("총 CPU 사이클", 
            metrics1.TotalCpuCycles, 
            metrics2.TotalCpuCycles, 
            "", 
            lowerIsBetter: true);

        _output.WriteLine("");
        _output.WriteLine("─────────────────────────────────────────────────────────────────────");
        _output.WriteLine("");

        // 전체적인 승자 판단
        int winrt1Score = 0;
        int winrt2Score = 0;

        if (metrics1.AverageCpuPercent < metrics2.AverageCpuPercent) winrt1Score++; else winrt2Score++;
        if (metrics1.AverageMemoryMB < metrics2.AverageMemoryMB) winrt1Score++; else winrt2Score++;
        if (metrics1.AverageWorkingSetMB < metrics2.AverageWorkingSetMB) winrt1Score++; else winrt2Score++;

        _output.WriteLine("🏆 전체 평가:");
        _output.WriteLine($"   BorderService_Test_winrt (개별 창): {winrt1Score}점");
        _output.WriteLine($"   BorderService_test_winrt2 (DComp 오버레이): {winrt2Score}점");
        
        if (winrt1Score > winrt2Score)
        {
            _output.WriteLine("\n   ✓ BorderService_Test_winrt 방식이 더 효율적입니다.");
        }
        else if (winrt2Score > winrt1Score)
        {
            _output.WriteLine("\n   ✓ BorderService_test_winrt2 방식이 더 효율적입니다.");
        }
        else
        {
            _output.WriteLine("\n   ≈ 두 방식의 성능이 비슷합니다.");
        }
    }

    private void PrintComparison(string metric, double value1, double value2, string unit, bool lowerIsBetter)
    {
        double diff = value2 - value1;
        double diffPercent = value1 != 0 ? (diff / value1) * 100 : 0;

        string winner;
        string diffSign;

        if (Math.Abs(diffPercent) < 5)
        {
            winner = "≈";
            diffSign = "±";
        }
        else if ((lowerIsBetter && value1 < value2) || (!lowerIsBetter && value1 > value2))
        {
            winner = "✓ Test_winrt";
            diffSign = lowerIsBetter ? "+" : "-";
        }
        else
        {
            winner = "✓ test_winrt2";
            diffSign = lowerIsBetter ? "-" : "+";
        }

        _output.WriteLine($"   {metric}:");
        _output.WriteLine($"      Test_winrt:   {value1:F2} {unit}");
        _output.WriteLine($"      test_winrt2:  {value2:F2} {unit}");
        _output.WriteLine($"      차이:         {diffSign}{Math.Abs(diffPercent):F1}%");
        _output.WriteLine($"      우수:         {winner}");
        _output.WriteLine("");
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
            }
            catch { }
        }
        _processesToCleanup.Clear();
    }

    private class PerformanceMetrics
    {
        public double AverageCpuPercent { get; set; }
        public double MaxCpuPercent { get; set; }
        public double MinCpuPercent { get; set; }
        
        public double AverageMemoryMB { get; set; }
        public double MaxMemoryMB { get; set; }
        public double MinMemoryMB { get; set; }
        
        public double AverageWorkingSetMB { get; set; }
        public double MaxWorkingSetMB { get; set; }
        
        public ulong TotalCpuCycles { get; set; }
        
        public long MeasurementDurationMs { get; set; }
        public int SampleCount { get; set; }
    }
}
