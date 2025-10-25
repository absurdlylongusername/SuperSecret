@echo off
REM Setup script for SuperSecret application (Windows)

echo SuperSecret - Setup Script
echo ==========================
echo.

REM Check if .NET is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo Error: .NET SDK is not installed. Please install .NET 9 SDK first.
    exit /b 1
)

echo Step 1: Setting up User Secrets...
cd SuperSecret

REM Generate a random signing key (using PowerShell)
for /f "delims=" %%i in ('powershell -Command "[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))"') do set SIGNING_KEY=%%i

REM Set user secrets
dotnet user-secrets set "TokenSigningKey" "%SIGNING_KEY%"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=SuperSecretDb;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;"

echo [32m? User secrets configured[0m
echo.

echo Step 2: Starting SQL Server (Docker)...
cd ..
docker-compose up -d

echo Waiting for SQL Server to be ready (30 seconds)...
timeout /t 30 /nobreak >nul

echo [32m? SQL Server started[0m
echo.

echo Step 3: Creating database schema...
REM Check if sqlcmd is available
where sqlcmd >nul 2>&1
if errorlevel 1 (
    echo [33m? sqlcmd not found. Please run SuperSecret/DatabaseSchema.sql manually using:[0m
    echo   - SQL Server Management Studio
    echo- Azure Data Studio
    echo   - Or install sqlcmd tools
) else (
    sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -i SuperSecret\DatabaseSchema.sql -C
    echo [32m? Database schema created[0m
)

echo.
echo ==========================
echo Setup Complete!
echo ==========================
echo.
echo To start the application:
echo   cd SuperSecret
echo   dotnet run
echo.
echo Then navigate to:
echo   - Admin UI: https://localhost:7xxx/admin
echo   - API: POST https://localhost:7xxx/api/links
echo.
pause
