# BorderService 성능 비교 테스트 실행 스크립트

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BorderService 성능 비교 테스트 도구" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# 경로 설정
$BorderServiceTestWinrtPath = "C:\Users\subin\source\repos\BorderService_Test_winrt\BorderService_Test_winrt"
$BorderServiceTestWinrt2Path = ".\CustomWindow\BorderService_test_winrt2"
$TestProjectPath = ".\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj"

# 함수: 경로 확인
function Test-ProjectPath {
    param($Path, $Name)
    
    if (Test-Path $Path) {
        Write-Host "? $Name 경로 확인됨: $Path" -ForegroundColor Green
        return $true
    } else {
        Write-Host "? $Name 경로를 찾을 수 없습니다: $Path" -ForegroundColor Red
        return $false
    }
}

# 함수: 빌드 확인
function Test-BuildExists {
    param($BasePath, $Name)
    
    $possiblePaths = @(
        "$BasePath\x64\Release\*.exe",
        "$BasePath\Release\*.exe",
        "$BasePath\$Name\x64\Release\*.exe",
        "$BasePath\$Name\Release\*.exe"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $exe = Get-Item $path | Select-Object -First 1
            Write-Host "  → 실행 파일: $($exe.FullName)" -ForegroundColor Gray
            return $true
        }
    }
    
    return $false
}

Write-Host "1??  사전 확인" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

$test1Path = Test-ProjectPath $BorderServiceTestWinrtPath "BorderService_Test_winrt"
$test2Path = Test-ProjectPath $BorderServiceTestWinrt2Path "BorderService_test_winrt2"

if (-not $test1Path) {
    Write-Host ""
    Write-Host "??  BorderService_Test_winrt 경로를 수정해주세요:" -ForegroundColor Yellow
    Write-Host "    스크립트 내 `$BorderServiceTestWinrtPath 변수 수정 필요" -ForegroundColor Gray
}

Write-Host ""

# 빌드 확인
Write-Host "2??  빌드 파일 확인" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

if ($test1Path) {
    Write-Host "BorderService_Test_winrt:" -ForegroundColor Cyan
    $build1 = Test-BuildExists $BorderServiceTestWinrtPath "BorderService_Test_winrt"
    if (-not $build1) {
        Write-Host "  ? Release 빌드를 찾을 수 없습니다" -ForegroundColor Red
        Write-Host "    → Visual Studio에서 Release/x64로 빌드하세요" -ForegroundColor Yellow
    }
}

if ($test2Path) {
    Write-Host "BorderService_test_winrt2:" -ForegroundColor Cyan
    $build2 = Test-BuildExists $BorderServiceTestWinrt2Path "BorderService_test_winrt2"
    if (-not $build2) {
        Write-Host "  ? Release 빌드를 찾을 수 없습니다" -ForegroundColor Red
        Write-Host "    → Visual Studio에서 Release/x64로 빌드하세요" -ForegroundColor Yellow
    }
}

Write-Host ""

# 테스트 옵션 선택
Write-Host "3??  테스트 선택" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray
Write-Host "1. 기본 성능 비교 테스트 (10초)" -ForegroundColor White
Write-Host "2. 고급 성능 비교 테스트 (GPU 포함, 15초)" -ForegroundColor White
Write-Host "3. 부하 테스트 (다중 창)" -ForegroundColor White
Write-Host "4. 모든 테스트 실행" -ForegroundColor White
Write-Host "0. 종료" -ForegroundColor Gray
Write-Host ""

$choice = Read-Host "선택 (1-4)"

$filter = ""
switch ($choice) {
    "1" { $filter = "DisplayName~성능 비교: BorderService" }
    "2" { $filter = "DisplayName~고급 성능 비교" }
    "3" { $filter = "DisplayName~부하 테스트" }
    "4" { $filter = "Category=Performance|Category=AdvancedPerformance|Category=LoadTest" }
    "0" { 
        Write-Host "종료합니다." -ForegroundColor Gray
        exit 
    }
    default {
        Write-Host "잘못된 선택입니다. 모든 테스트를 실행합니다." -ForegroundColor Yellow
        $filter = "Category=Performance|Category=AdvancedPerformance|Category=LoadTest"
    }
}

Write-Host ""
Write-Host "4??  테스트 실행" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray
Write-Host ""

# 테스트 실행
if ($filter -eq "") {
    dotnet test $TestProjectPath --logger "console;verbosity=detailed"
} else {
    dotnet test $TestProjectPath --filter $filter --logger "console;verbosity=detailed"
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  테스트 완료" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "?? 팁:" -ForegroundColor Yellow
Write-Host "  - 더 정확한 측정을 위해 다른 프로그램을 종료하세요" -ForegroundColor Gray
Write-Host "  - 테스트 중 창을 여러 개 열어두면 부하 테스트와 유사한 환경이 됩니다" -ForegroundColor Gray
Write-Host "  - GPU 측정이 안 되면 관리자 권한으로 실행해보세요" -ForegroundColor Gray
Write-Host ""
