param(
    [string] $Server = "(localdb)\MSSQLLocalDB",
    [string] $Database = "SuperSecretDb",
    [switch] $UseSqlAuth,
    [string] $Username,
    [string] $Password,
    [switch] $SetUserSecrets
)

$ErrorActionPreference = "Stop"

function Test-Command($name) {
    try { Get-Command $name -ErrorAction Stop | Out-Null; return $true } catch { return $false }
}

function Exec-Sql([string]$inputFile, [string]$db = $null) {
    $useInvoke = Test-Command "Invoke-Sqlcmd"
    if ($useInvoke) {
        $params = @{
            ServerInstance = $Server
            InputFile      = $inputFile
            TrustServerCertificate = $true
        }
        if ($db) { $params.Database = $db }
        if ($UseSqlAuth) { $params.Username = $Username; $params.Password = $Password }
        Invoke-Sqlcmd @params
    } else {
        if (-not (Test-Command "sqlcmd")) {
            throw "Neither Invoke-Sqlcmd (SqlServer PowerShell module) nor sqlcmd is available. Install one of them and retry."
        }
        $args = @("-S", $Server, "-i", $inputFile, "-C") # -C => TrustServerCertificate
        if ($db) { $args += @("-d", $db) }
        if ($UseSqlAuth) { $args += @("-U", $Username, "-P", $Password) } else { $args += @("-E") }
        & sqlcmd @args | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $inputFile" }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$schemaFile = Join-Path $repoRoot "SuperSecret\DatabaseSchema.sql"
$procsDir   = Join-Path $repoRoot "SuperSecretDatabase\Stored Procedures"

if (-not (Test-Path $schemaFile)) { throw "Schema file not found: $schemaFile" }
if (-not (Test-Path $procsDir))   { throw "Stored procedures directory not found: $procsDir" }

Write-Host "Running schema script against server '$Server'..." -ForegroundColor Cyan
Exec-Sql -inputFile $schemaFile # Contains USE [SuperSecretDb]

$procFiles = Get-ChildItem -Path $procsDir -Filter *.sql | Sort-Object Name
if ($procFiles.Count -gt 0) {
    Write-Host "Creating/Updating stored procedures in database '$Database'..." -ForegroundColor Cyan
    foreach ($f in $procFiles) {
        Write-Host " - $($f.Name)"
        Exec-Sql -inputFile $f.FullName -db $Database
    }
} else {
    Write-Host "No stored procedures found in '$procsDir'." -ForegroundColor Yellow
}

if ($SetUserSecrets) {
    $webProj = Join-Path $repoRoot "SuperSecret"
    if (-not (Test-Path $webProj)) { throw "Web project directory not found: $webProj" }

    if ($UseSqlAuth) {
        $conn = "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;TrustServerCertificate=True;"
    } else {
        $conn = "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"
    }

    Push-Location $webProj
    try {
        & dotnet user-secrets set "ConnectionStrings:DefaultConnection" "$conn"
        Write-Host "User secret ConnectionStrings:DefaultConnection set." -ForegroundColor Green
    } finally {
        Pop-Location
    }
}

Write-Host "Database setup complete." -ForegroundColor Green