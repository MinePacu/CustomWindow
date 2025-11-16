using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace CustomWindow.Tests;

/// <summary>
/// 성능 테스트 결과를 CSV 파일로 저장하는 유틸리티
/// </summary>
public class PerformanceResultExporter
{
    public static void ExportToCsv(string filePath, PerformanceResult result1, PerformanceResult result2)
    {
        var csv = new StringBuilder();
        
        // 헤더
        csv.AppendLine("Metric,BorderService_Test_winrt,BorderService_test_winrt2,Difference_Percent,Winner");
        
        // CPU
        csv.AppendLine($"Average CPU %,{result1.AverageCpuPercent:F2},{result2.AverageCpuPercent:F2}," +
            $"{GetDifferencePercent(result1.AverageCpuPercent, result2.AverageCpuPercent):F2}," +
            $"{GetWinner(result1.AverageCpuPercent, result2.AverageCpuPercent, true)}");
        
        csv.AppendLine($"Max CPU %,{result1.MaxCpuPercent:F2},{result2.MaxCpuPercent:F2}," +
            $"{GetDifferencePercent(result1.MaxCpuPercent, result2.MaxCpuPercent):F2}," +
            $"{GetWinner(result1.MaxCpuPercent, result2.MaxCpuPercent, true)}");
        
        // 메모리
        csv.AppendLine($"Average Memory MB,{result1.AverageMemoryMB:F2},{result2.AverageMemoryMB:F2}," +
            $"{GetDifferencePercent(result1.AverageMemoryMB, result2.AverageMemoryMB):F2}," +
            $"{GetWinner(result1.AverageMemoryMB, result2.AverageMemoryMB, true)}");
        
        csv.AppendLine($"Max Memory MB,{result1.MaxMemoryMB:F2},{result2.MaxMemoryMB:F2}," +
            $"{GetDifferencePercent(result1.MaxMemoryMB, result2.MaxMemoryMB):F2}," +
            $"{GetWinner(result1.MaxMemoryMB, result2.MaxMemoryMB, true)}");
        
        // Working Set
        csv.AppendLine($"Average WorkingSet MB,{result1.AverageWorkingSetMB:F2},{result2.AverageWorkingSetMB:F2}," +
            $"{GetDifferencePercent(result1.AverageWorkingSetMB, result2.AverageWorkingSetMB):F2}," +
            $"{GetWinner(result1.AverageWorkingSetMB, result2.AverageWorkingSetMB, true)}");
        
        // GPU (있는 경우)
        if (result1.HasGpuData && result2.HasGpuData)
        {
            csv.AppendLine($"Average GPU %,{result1.AverageGpuPercent:F2},{result2.AverageGpuPercent:F2}," +
                $"{GetDifferencePercent(result1.AverageGpuPercent, result2.AverageGpuPercent):F2}," +
                $"{GetWinner(result1.AverageGpuPercent, result2.AverageGpuPercent, true)}");
        }
        
        // 핸들/스레드
        if (result1.AverageHandleCount > 0 && result2.AverageHandleCount > 0)
        {
            csv.AppendLine($"Average Handle Count,{result1.AverageHandleCount:F0},{result2.AverageHandleCount:F0}," +
                $"{GetDifferencePercent(result1.AverageHandleCount, result2.AverageHandleCount):F2}," +
                $"{GetWinner(result1.AverageHandleCount, result2.AverageHandleCount, true)}");
        }
        
        // 메타 정보
        csv.AppendLine();
        csv.AppendLine("Test Information");
        csv.AppendLine($"Test Date,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine($"Test Duration (ms),{result1.MeasurementDurationMs}");
        csv.AppendLine($"Sample Count,{result1.SampleCount}");
        csv.AppendLine($"Computer Name,{Environment.MachineName}");
        csv.AppendLine($"Processor Count,{Environment.ProcessorCount}");
        csv.AppendLine($"OS Version,{Environment.OSVersion}");
        
        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 시계열 데이터를 CSV로 내보내기 (그래프 생성용)
    /// </summary>
    public static void ExportTimeSeriesData(
        string filePath,
        List<TimeSeriesSample> samples1,
        List<TimeSeriesSample> samples2,
        string name1 = "Test_winrt",
        string name2 = "test_winrt2")
    {
        var csv = new StringBuilder();
        
        // 헤더
        csv.AppendLine($"Time_Sec,{name1}_CPU%,{name1}_Memory_MB,{name1}_GPU%," +
                       $"{name2}_CPU%,{name2}_Memory_MB,{name2}_GPU%");
        
        // 두 샘플 리스트의 최대 길이 사용
        int maxCount = Math.Max(samples1.Count, samples2.Count);
        
        for (int i = 0; i < maxCount; i++)
        {
            var s1 = i < samples1.Count ? samples1[i] : null;
            var s2 = i < samples2.Count ? samples2[i] : null;
            
            double time = s1?.ElapsedSeconds ?? s2?.ElapsedSeconds ?? 0;
            
            string s1Cpu = s1?.CpuPercent.ToString("F2") ?? "";
            string s1Mem = s1?.MemoryMB.ToString("F2") ?? "";
            string s1Gpu = s1?.GpuPercent.ToString("F2") ?? "";
            
            string s2Cpu = s2?.CpuPercent.ToString("F2") ?? "";
            string s2Mem = s2?.MemoryMB.ToString("F2") ?? "";
            string s2Gpu = s2?.GpuPercent.ToString("F2") ?? "";
            
            csv.AppendLine($"{time:F2},{s1Cpu},{s1Mem},{s1Gpu},{s2Cpu},{s2Mem},{s2Gpu}");
        }
        
        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 단일 프로세스의 시계열 데이터를 CSV로 내보내기
    /// </summary>
    public static void ExportSingleTimeSeriesData(
        string filePath,
        List<TimeSeriesSample> samples,
        string name = "Process")
    {
        var csv = new StringBuilder();
        
        // 헤더
        csv.AppendLine("Time_Sec,CPU%,Memory_MB,WorkingSet_MB,GPU%,Handle_Count,Thread_Count");
        
        foreach (var sample in samples)
        {
            csv.AppendLine($"{sample.ElapsedSeconds:F2}," +
                          $"{sample.CpuPercent:F2}," +
                          $"{sample.MemoryMB:F2}," +
                          $"{sample.WorkingSetMB:F2}," +
                          $"{sample.GpuPercent:F2}," +
                          $"{sample.HandleCount}," +
                          $"{sample.ThreadCount}");
        }
        
        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
    }
    
    private static double GetDifferencePercent(double value1, double value2)
    {
        if (value1 == 0) return 0;
        return ((value2 - value1) / value1) * 100;
    }
    
    private static string GetWinner(double value1, double value2, bool lowerIsBetter)
    {
        double diff = Math.Abs(value2 - value1);
        double percent = value1 != 0 ? (diff / value1) * 100 : 0;
        
        if (percent < 5) return "Tie";
        
        if (lowerIsBetter)
            return value1 < value2 ? "Test_winrt" : "test_winrt2";
        else
            return value1 > value2 ? "Test_winrt" : "test_winrt2";
    }

    /// <summary>
    /// 시계열 샘플 데이터
    /// </summary>
    public class TimeSeriesSample
    {
        public double ElapsedSeconds { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryMB { get; set; }
        public double WorkingSetMB { get; set; }
        public double GpuPercent { get; set; }
        public int HandleCount { get; set; }
        public int ThreadCount { get; set; }
    }
    
    public class PerformanceResult
    {
        public double AverageCpuPercent { get; set; }
        public double MaxCpuPercent { get; set; }
        public double MinCpuPercent { get; set; }
        
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
        public List<TimeSeriesSample>? TimeSeriesData { get; set; }
    }
}
