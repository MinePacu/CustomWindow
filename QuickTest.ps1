# 빠른 성능 테스트 실행
# 기본 성능 비교만 실행합니다.

Write-Host "BorderService 성능 비교 테스트 (빠른 실행)" -ForegroundColor Cyan
Write-Host ""

dotnet test .\CustomWindow\CustomWindow.Tests\CustomWindow.Tests.csproj `
    --filter "DisplayName~성능 비교: BorderService" `
    --logger "console;verbosity=detailed"

Write-Host ""
Write-Host "완료! 더 많은 옵션은 RunPerformanceTests.ps1을 사용하세요." -ForegroundColor Green
