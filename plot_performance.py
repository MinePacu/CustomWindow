"""
BorderService 성능 비교 그래프 생성 스크립트

CSV 파일에서 시계열 데이터를 읽어 그래프를 생성합니다.

사용법:
    python plot_performance.py performance_comparison_timeseries.csv
    python plot_performance.py performance_test_winrt_timeseries.csv
    
필요한 패키지:
    pip install matplotlib pandas
"""

import sys
import os
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
from datetime import datetime

def plot_comparison_timeseries(csv_file):
    """두 방식을 비교하는 시계열 그래프 생성"""
    
    # CSV 읽기
    df = pd.read_csv(csv_file)
    
    # 그래프 설정
    plt.style.use('seaborn-v0_8-darkgrid')
    fig, axes = plt.subplots(3, 1, figsize=(14, 10))
    fig.suptitle('BorderService Performance Comparison Over Time', fontsize=16, fontweight='bold')
    
    # CPU 사용률 그래프
    ax1 = axes[0]
    ax1.plot(df['Time_Sec'], df['Test_winrt_CPU%'], 
             label='Test_winrt (Individual Windows)', 
             color='#2E86AB', linewidth=2, marker='o', markersize=3)
    ax1.plot(df['Time_Sec'], df['test_winrt2_CPU%'], 
             label='test_winrt2 (DirectComposition)', 
             color='#A23B72', linewidth=2, marker='s', markersize=3)
    ax1.set_ylabel('CPU Usage (%)', fontsize=12, fontweight='bold')
    ax1.set_xlabel('Time (seconds)', fontsize=10)
    ax1.legend(loc='upper right', fontsize=10)
    ax1.grid(True, alpha=0.3)
    ax1.set_title('CPU Usage Comparison', fontsize=12, fontweight='bold', pad=10)
    
    # 메모리 사용량 그래프
    ax2 = axes[1]
    ax2.plot(df['Time_Sec'], df['Test_winrt_Memory_MB'], 
             label='Test_winrt (Individual Windows)', 
             color='#2E86AB', linewidth=2, marker='o', markersize=3)
    ax2.plot(df['Time_Sec'], df['test_winrt2_Memory_MB'], 
             label='test_winrt2 (DirectComposition)', 
             color='#A23B72', linewidth=2, marker='s', markersize=3)
    ax2.set_ylabel('Memory Usage (MB)', fontsize=12, fontweight='bold')
    ax2.set_xlabel('Time (seconds)', fontsize=10)
    ax2.legend(loc='upper right', fontsize=10)
    ax2.grid(True, alpha=0.3)
    ax2.set_title('Memory Usage Comparison', fontsize=12, fontweight='bold', pad=10)
    
    # GPU 사용률 그래프 (데이터가 있는 경우)
    ax3 = axes[2]
    if df['Test_winrt_GPU%'].notna().any() or df['test_winrt2_GPU%'].notna().any():
        ax3.plot(df['Time_Sec'], df['Test_winrt_GPU%'], 
                 label='Test_winrt (Individual Windows)', 
                 color='#2E86AB', linewidth=2, marker='o', markersize=3)
        ax3.plot(df['Time_Sec'], df['test_winrt2_GPU%'], 
                 label='test_winrt2 (DirectComposition)', 
                 color='#A23B72', linewidth=2, marker='s', markersize=3)
        ax3.set_ylabel('GPU Usage (%)', fontsize=12, fontweight='bold')
        ax3.set_xlabel('Time (seconds)', fontsize=10)
        ax3.legend(loc='upper right', fontsize=10)
        ax3.grid(True, alpha=0.3)
        ax3.set_title('GPU Usage Comparison', fontsize=12, fontweight='bold', pad=10)
    else:
        ax3.text(0.5, 0.5, 'GPU data not available', 
                ha='center', va='center', fontsize=14, color='gray')
        ax3.set_xticks([])
        ax3.set_yticks([])
    
    plt.tight_layout()
    
    # 파일명 생성
    output_file = csv_file.replace('.csv', '_graph.png')
    plt.savefig(output_file, dpi=300, bbox_inches='tight')
    print(f"? 그래프 저장됨: {output_file}")
    
    # 그래프 표시
    plt.show()

def plot_single_timeseries(csv_file):
    """단일 프로세스의 시계열 그래프 생성"""
    
    # CSV 읽기
    df = pd.read_csv(csv_file)
    
    # 프로세스 이름 추출
    process_name = "Process"
    if "test_winrt" in csv_file:
        process_name = "Test_winrt" if "test_winrt2" not in csv_file else "test_winrt2"
    
    # 그래프 설정
    plt.style.use('seaborn-v0_8-darkgrid')
    fig, axes = plt.subplots(3, 2, figsize=(16, 12))
    fig.suptitle(f'{process_name} Performance Monitoring', fontsize=16, fontweight='bold')
    
    # CPU 사용률
    ax1 = axes[0, 0]
    ax1.plot(df['Time_Sec'], df['CPU%'], color='#2E86AB', linewidth=2)
    ax1.fill_between(df['Time_Sec'], df['CPU%'], alpha=0.3, color='#2E86AB')
    ax1.set_ylabel('CPU Usage (%)', fontsize=11, fontweight='bold')
    ax1.set_xlabel('Time (seconds)', fontsize=9)
    ax1.grid(True, alpha=0.3)
    ax1.set_title('CPU Usage', fontsize=11, fontweight='bold')
    
    # 메모리 사용량 (Private)
    ax2 = axes[0, 1]
    ax2.plot(df['Time_Sec'], df['Memory_MB'], color='#A23B72', linewidth=2)
    ax2.fill_between(df['Time_Sec'], df['Memory_MB'], alpha=0.3, color='#A23B72')
    ax2.set_ylabel('Memory (MB)', fontsize=11, fontweight='bold')
    ax2.set_xlabel('Time (seconds)', fontsize=9)
    ax2.grid(True, alpha=0.3)
    ax2.set_title('Private Memory', fontsize=11, fontweight='bold')
    
    # Working Set
    ax3 = axes[1, 0]
    ax3.plot(df['Time_Sec'], df['WorkingSet_MB'], color='#F18F01', linewidth=2)
    ax3.fill_between(df['Time_Sec'], df['WorkingSet_MB'], alpha=0.3, color='#F18F01')
    ax3.set_ylabel('Working Set (MB)', fontsize=11, fontweight='bold')
    ax3.set_xlabel('Time (seconds)', fontsize=9)
    ax3.grid(True, alpha=0.3)
    ax3.set_title('Working Set', fontsize=11, fontweight='bold')
    
    # GPU 사용률
    ax4 = axes[1, 1]
    if df['GPU%'].notna().any() and df['GPU%'].max() > 0:
        ax4.plot(df['Time_Sec'], df['GPU%'], color='#06A77D', linewidth=2)
        ax4.fill_between(df['Time_Sec'], df['GPU%'], alpha=0.3, color='#06A77D')
        ax4.set_ylabel('GPU Usage (%)', fontsize=11, fontweight='bold')
        ax4.set_xlabel('Time (seconds)', fontsize=9)
        ax4.grid(True, alpha=0.3)
        ax4.set_title('GPU Usage', fontsize=11, fontweight='bold')
    else:
        ax4.text(0.5, 0.5, 'GPU data not available', 
                ha='center', va='center', fontsize=12, color='gray')
        ax4.set_xticks([])
        ax4.set_yticks([])
    
    # 핸들 수
    ax5 = axes[2, 0]
    ax5.plot(df['Time_Sec'], df['Handle_Count'], color='#C73E1D', linewidth=2)
    ax5.set_ylabel('Handle Count', fontsize=11, fontweight='bold')
    ax5.set_xlabel('Time (seconds)', fontsize=9)
    ax5.grid(True, alpha=0.3)
    ax5.set_title('System Handles', fontsize=11, fontweight='bold')
    
    # 스레드 수
    ax6 = axes[2, 1]
    ax6.plot(df['Time_Sec'], df['Thread_Count'], color='#5E2CA5', linewidth=2, marker='o', markersize=4)
    ax6.set_ylabel('Thread Count', fontsize=11, fontweight='bold')
    ax6.set_xlabel('Time (seconds)', fontsize=9)
    ax6.grid(True, alpha=0.3)
    ax6.set_title('Thread Count', fontsize=11, fontweight='bold')
    
    plt.tight_layout()
    
    # 파일명 생성
    output_file = csv_file.replace('.csv', '_graph.png')
    plt.savefig(output_file, dpi=300, bbox_inches='tight')
    print(f"? 그래프 저장됨: {output_file}")
    
    # 그래프 표시
    plt.show()

def plot_summary_comparison(csv_file):
    """요약 비교 차트 생성"""
    
    df = pd.read_csv(csv_file)
    
    # 평균값 계산
    metrics = {
        'CPU (%)': [
            df['Test_winrt_CPU%'].mean(),
            df['test_winrt2_CPU%'].mean()
        ],
        'Memory (MB)': [
            df['Test_winrt_Memory_MB'].mean(),
            df['test_winrt2_Memory_MB'].mean()
        ]
    }
    
    if df['Test_winrt_GPU%'].notna().any():
        metrics['GPU (%)'] = [
            df['Test_winrt_GPU%'].mean(),
            df['test_winrt2_GPU%'].mean()
        ]
    
    # 바 차트
    fig, ax = plt.subplots(1, len(metrics), figsize=(14, 6))
    fig.suptitle('Average Performance Comparison', fontsize=16, fontweight='bold')
    
    labels = ['Test_winrt', 'test_winrt2']
    colors = ['#2E86AB', '#A23B72']
    
    for idx, (metric_name, values) in enumerate(metrics.items()):
        ax_current = ax[idx] if len(metrics) > 1 else ax
        bars = ax_current.bar(labels, values, color=colors, alpha=0.8, edgecolor='black')
        ax_current.set_ylabel(metric_name, fontsize=11, fontweight='bold')
        ax_current.set_title(f'Average {metric_name}', fontsize=11, fontweight='bold')
        ax_current.grid(True, axis='y', alpha=0.3)
        
        # 값 표시
        for bar, value in zip(bars, values):
            height = bar.get_height()
            ax_current.text(bar.get_x() + bar.get_width()/2., height,
                           f'{value:.2f}',
                           ha='center', va='bottom', fontsize=10, fontweight='bold')
    
    plt.tight_layout()
    
    # 파일명 생성
    output_file = csv_file.replace('.csv', '_summary.png')
    plt.savefig(output_file, dpi=300, bbox_inches='tight')
    print(f"? 요약 차트 저장됨: {output_file}")
    
    plt.show()

def main():
    if len(sys.argv) < 2:
        print("사용법: python plot_performance.py <csv_file>")
        print("\n예시:")
        print("  python plot_performance.py performance_comparison_timeseries.csv")
        print("  python plot_performance.py performance_test_winrt_timeseries.csv")
        sys.exit(1)
    
    csv_file = sys.argv[1]
    
    if not os.path.exists(csv_file):
        print(f"? 파일을 찾을 수 없습니다: {csv_file}")
        sys.exit(1)
    
    print(f"?? 그래프 생성 중: {csv_file}")
    
    # CSV 파일 헤더 확인하여 타입 판단
    df = pd.read_csv(csv_file, nrows=0)
    columns = df.columns.tolist()
    
    if 'Test_winrt_CPU%' in columns and 'test_winrt2_CPU%' in columns:
        # 비교 그래프
        print("→ 비교 그래프 생성 중...")
        plot_comparison_timeseries(csv_file)
        plot_summary_comparison(csv_file)
    elif 'CPU%' in columns:
        # 단일 프로세스 그래프
        print("→ 단일 프로세스 그래프 생성 중...")
        plot_single_timeseries(csv_file)
    else:
        print(f"? 알 수 없는 CSV 형식입니다.")
        print(f"   컬럼: {columns}")
        sys.exit(1)
    
    print("\n? 완료!")

if __name__ == "__main__":
    main()
