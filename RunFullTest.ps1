# BorderService 성능 테스트 및 그래프 생성 통합 스크립트
# 테스트 실행 → CSV 생성 → 그래프 생성까지 자동화

param(
    [switch]$SkipTest,      # 테스트를 건너뛰고 기존 CSV로 그래프만 생성
    [switch]$NoGraph,       # 그래프 생성 건너뛰기
    [switch]$AutoOpen       # 생성된 그래프 자동 열기
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?  BorderService 성능 테스트 및 시각화 통합 도구                 ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# 1. 사전 확인
# ============================================================================

Write-Host "?? 사전 확인" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor Gray

# .NET SDK 확인
try {
    $dotnetVersion = dotnet --version
    Write-Host "? .NET SDK: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "? .NET SDK를 찾을 수 없습니다." -ForegroundColor Red
    exit 1
}

# Python 확인 (그래프 생성 시)
if (-not $NoGraph) {
    try {
        $pythonVersion = python --version 2>&1
        Write-Host "? Python: $pythonVersion" -ForegroundColor Green
    } catch {
        Write-Host "? Python을 찾을 수 없습니다." -ForegroundColor Red
        Write-Host "  그래프 생성을 건너뛰려면 -NoGraph 옵션을 사용하세요." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""

# ============================================================================
# 2. 성능 테스트 실행
# ============================================================================

if (-not $SkipTest) {
    Write-Host "?? 성능 테스트 실행" -ForegroundColor Yellow
    Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor Gray
    
    # 기존 CSV 파일 백업
    $csvFiles = Get-ChildItem -Path . -Filter "*timeseries.csv" -File
    if ($csvFiles.Count -gt 0) {
        $backupDir = ".\performance_backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        $csvFiles | ForEach-Object {
            Move-Item $_.FullName -Destination $backupDir -Force
        }
        Write-Host "? 기존 CSV 파일 백업: $backupDir" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "테스트 실행 중... (약 20-30초 소요)" -ForegroundColor Cyan
    Write-Host ""
    
    try {
        dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
            --filter "DisplayName~고급 성능 비교" `
            --logger "console;verbosity=normal"
        
        Write-Host ""
        Write-Host "? 테스트 완료" -ForegroundColor Green
    } catch {
        Write-Host ""
        Write-Host "? 테스트 실패" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "??  테스트 건너뛰기 (기존 CSV 사용)" -ForegroundColor Yellow
}

Write-Host ""

# ============================================================================
# 3. CSV 파일 확인
# ============================================================================

Write-Host "?? 생성된 CSV 파일 확인" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor Gray

$csvFiles = Get-ChildItem -Path . -Filter "*timeseries.csv" -File

if ($csvFiles.Count -eq 0) {
    Write-Host "? CSV 파일을 찾을 수 없습니다." -ForegroundColor Red
    Write-Host "  테스트가 정상적으로 실행되지 않았을 수 있습니다." -ForegroundColor Yellow
    exit 1
}

Write-Host "? CSV 파일 발견: $($csvFiles.Count)개" -ForegroundColor Green
$csvFiles | ForEach-Object {
    $size = [math]::Round($_.Length / 1KB, 2)
    Write-Host "  → $($_.Name) ($size KB)" -ForegroundColor Gray
}

Write-Host ""

# ============================================================================
# 4. Python 패키지 설치 (필요시)
# ============================================================================

if (-not $NoGraph) {
    Write-Host "?? Python 패키지 확인" -ForegroundColor Yellow
    Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor Gray
    
    try {
        python -c "import matplotlib, pandas" 2>&1 | Out-Null
        Write-Host "? 필요한 패키지 설치됨" -ForegroundColor Green
    } catch {
        Write-Host "??  패키지 설치 중..." -ForegroundColor Cyan
        try {
            python -m pip install --upgrade pip --quiet
            python -m pip install matplotlib pandas --quiet
            Write-Host "? 패키지 설치 완료" -ForegroundColor Green
        } catch {
            Write-Host "? 패키지 설치 실패" -ForegroundColor Red
            Write-Host "  수동 설치: pip install matplotlib pandas" -ForegroundColor Yellow
            exit 1
        }
    }
    
    Write-Host ""
}

# ============================================================================
# 5. 그래프 생성
# ============================================================================

if (-not $NoGraph) {
    Write-Host "?? 그래프 생성" -ForegroundColor Yellow
    Write-Host "─────────────────────────────────────────────────────────────────" -ForegroundColor Gray
    Write-Host ""
    
    $graphCount = 0
    
    foreach ($csvFile in $csvFiles) {
        Write-Host "처리 중: $($csvFile.Name)" -ForegroundColor Cyan
        
        try {
            python plot_performance.py $csvFile.Name 2>&1 | Out-Null
            $graphCount++
            Write-Host "  ? 그래프 생성 완료" -ForegroundColor Green
        } catch {
            Write-Host "  ? 그래프 생성 실패: $($_.Exception.Message)" -ForegroundColor Red
        }
        
        Write-Host ""
    }
    
    Write-Host "? 총 $graphCount 개의 그래프 생성 완료" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# 6. 결과 요약
# ============================================================================

Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?                         결과 요약                               ?" -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

Write-Host "?? 생성된 파일:" -ForegroundColor Yellow
Write-Host ""

# CSV 파일
Write-Host "  CSV 데이터 파일:" -ForegroundColor White
$csvFiles | ForEach-Object {
    Write-Host "    ? $($_.Name)" -ForegroundColor Gray
}

# 그래프 파일
if (-not $NoGraph) {
    Write-Host ""
    Write-Host "  그래프 이미지:" -ForegroundColor White
    
    $graphFiles = Get-ChildItem -Path . -Filter "*_graph.png" -File
    $summaryFiles = Get-ChildItem -Path . -Filter "*_summary.png" -File
    
    $graphFiles | ForEach-Object {
        Write-Host "    ? $($_.Name)" -ForegroundColor Gray
    }
    
    $summaryFiles | ForEach-Object {
        Write-Host "    ? $($_.Name)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "?? 파일 위치: $PWD" -ForegroundColor White
Write-Host ""

# ============================================================================
# 7. 자동 열기
# ============================================================================

if ($AutoOpen -and -not $NoGraph) {
    Write-Host "???  그래프 자동 열기..." -ForegroundColor Yellow
    
    $imageFiles = Get-ChildItem -Path . -Filter "*_graph.png" -File
    $imageFiles += Get-ChildItem -Path . -Filter "*_summary.png" -File
    
    foreach ($img in $imageFiles | Select-Object -First 3) {
        Start-Process $img.FullName
        Start-Sleep -Milliseconds 500
    }
}

# ============================================================================
# 8. 다음 단계 안내
# ============================================================================

Write-Host "?? 다음 단계:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. 그래프 파일 열어서 성능 비교 확인" -ForegroundColor Gray
Write-Host "  2. CSV 파일을 Excel에서 열어 상세 분석" -ForegroundColor Gray
Write-Host "  3. 테스트 재실행: .\RunFullTest.ps1" -ForegroundColor Gray
Write-Host "  4. 그래프만 재생성: .\RunFullTest.ps1 -SkipTest" -ForegroundColor Gray
Write-Host ""

Write-Host "? 모든 작업 완료!" -ForegroundColor Green
Write-Host ""
