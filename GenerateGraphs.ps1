# 성능 그래프 생성 도구 설정 및 실행 스크립트

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  성능 그래프 생성 도구 설정" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Python 설치 확인
Write-Host "1??  Python 확인 중..." -ForegroundColor Yellow
try {
    $pythonVersion = python --version 2>&1
    Write-Host "? Python 설치됨: $pythonVersion" -ForegroundColor Green
} catch {
    Write-Host "? Python이 설치되지 않았습니다." -ForegroundColor Red
    Write-Host "  → https://www.python.org/downloads/ 에서 다운로드하세요." -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# 필요한 패키지 설치
Write-Host "2??  필요한 패키지 설치 중..." -ForegroundColor Yellow
Write-Host "   matplotlib, pandas 설치..." -ForegroundColor Gray

try {
    python -m pip install --upgrade pip --quiet
    python -m pip install matplotlib pandas --quiet
    Write-Host "? 패키지 설치 완료" -ForegroundColor Green
} catch {
    Write-Host "? 패키지 설치 실패" -ForegroundColor Red
    Write-Host "  수동 설치: pip install matplotlib pandas" -ForegroundColor Yellow
}

Write-Host ""

# CSV 파일 찾기
Write-Host "3??  CSV 파일 검색 중..." -ForegroundColor Yellow
$csvFiles = Get-ChildItem -Path . -Filter "*timeseries.csv" -File

if ($csvFiles.Count -eq 0) {
    Write-Host "? 시계열 CSV 파일을 찾을 수 없습니다." -ForegroundColor Red
    Write-Host "  먼저 성능 테스트를 실행하세요:" -ForegroundColor Yellow
    Write-Host "  → .\RunPerformanceTests.ps1" -ForegroundColor Gray
    Write-Host ""
    
    # 테스트 실행 옵션 제공
    $runTest = Read-Host "지금 성능 테스트를 실행하시겠습니까? (Y/N)"
    if ($runTest -eq "Y" -or $runTest -eq "y") {
        Write-Host ""
        Write-Host "성능 테스트 실행 중..." -ForegroundColor Cyan
        dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
            --filter "DisplayName~고급 성능 비교" `
            --logger "console;verbosity=normal"
        
        # 다시 CSV 파일 찾기
        Write-Host ""
        Write-Host "CSV 파일 재검색 중..." -ForegroundColor Yellow
        $csvFiles = Get-ChildItem -Path . -Filter "*timeseries.csv" -File
    } else {
        exit 0
    }
}

if ($csvFiles.Count -eq 0) {
    Write-Host "? 여전히 CSV 파일을 찾을 수 없습니다." -ForegroundColor Red
    exit 1
}

Write-Host "? CSV 파일 발견:" -ForegroundColor Green
$csvFiles | ForEach-Object {
    Write-Host "  → $($_.Name)" -ForegroundColor Gray
}

Write-Host ""

# 그래프 생성 옵션
Write-Host "4??  그래프 생성 옵션 선택" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

$index = 1
$csvFiles | ForEach-Object {
    Write-Host "$index. $($_.Name)" -ForegroundColor White
    $index++
}
Write-Host "0. 모든 파일 처리" -ForegroundColor White
Write-Host ""

$choice = Read-Host "선택 (0-$($csvFiles.Count))"

Write-Host ""
Write-Host "5??  그래프 생성 중..." -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

if ($choice -eq "0") {
    # 모든 파일 처리
    foreach ($file in $csvFiles) {
        Write-Host ""
        Write-Host "처리 중: $($file.Name)" -ForegroundColor Cyan
        python plot_performance.py $file.Name
    }
} else {
    # 선택한 파일만 처리
    $selectedIndex = [int]$choice - 1
    if ($selectedIndex -ge 0 -and $selectedIndex -lt $csvFiles.Count) {
        $selectedFile = $csvFiles[$selectedIndex]
        Write-Host ""
        Write-Host "처리 중: $($selectedFile.Name)" -ForegroundColor Cyan
        python plot_performance.py $selectedFile.Name
    } else {
        Write-Host "? 잘못된 선택입니다." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  완료!" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "생성된 그래프 파일:" -ForegroundColor Green
Get-ChildItem -Path . -Filter "*_graph.png" -File | ForEach-Object {
    Write-Host "  → $($_.Name)" -ForegroundColor Gray
}
Get-ChildItem -Path . -Filter "*_summary.png" -File | ForEach-Object {
    Write-Host "  → $($_.Name)" -ForegroundColor Gray
}
Write-Host ""
