# VRC Group Guardian Build Script
# This script builds the application locally for development and testing

param(
    [Parameter(HelpMessage="Build configuration (Debug/Release)")]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter(HelpMessage="Target runtime (win-x64/win-x86/win-arm64/portable)")]
    [ValidateSet("win-x64", "win-x86", "win-arm64", "portable")]
    [string]$Runtime = "win-x64",
    
    [Parameter(HelpMessage="Create single file executable")]
    [switch]$SingleFile,
    
    [Parameter(HelpMessage="Include all dependencies (self-contained)")]
    [switch]$SelfContained,
    
    [Parameter(HelpMessage="Skip tests before building")]
    [switch]$SkipTests,
    
    [Parameter(HelpMessage="Clean build output before building")]
    [switch]$Clean,
    
    [Parameter(HelpMessage="Verbose output")]
    [switch]$Verbose
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\VrcGroupGuardian\VrcGroupGuardian.csproj"
$OutputDir = Join-Path $ProjectRoot "build\output\$Runtime"
$TestDir = Join-Path $ProjectRoot "tests"

# Build information
$BuildNumber = Get-Date -Format "yyyyMMddHHmm"
$Version = "1.0.$BuildNumber.0"
$InformationalVersion = "$Version+$(git rev-parse --short HEAD 2>$null)"

Write-Host "🔨 VRC Group Guardian Build Script" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Runtime: $Runtime" -ForegroundColor Yellow
Write-Host "Version: $InformationalVersion" -ForegroundColor Yellow
Write-Host ""

# Validate prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Green

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET SDK: $dotnetVersion"
} catch {
    Write-Error "❌ .NET SDK not found. Please install .NET 8.0 SDK or later."
    exit 1
}

# Check project file
if (!(Test-Path $ProjectFile)) {
    Write-Error "❌ Project file not found: $ProjectFile"
    exit 1
}
Write-Host "✅ Project file found"

# Clean output directory
if ($Clean) {
    Write-Host "🧹 Cleaning output directory..." -ForegroundColor Green
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
}

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Run tests
if (!$SkipTests) {
    Write-Host "🧪 Running tests..." -ForegroundColor Green
    
    # Unit tests
    Write-Host "Running unit tests..." -ForegroundColor Yellow
    $unitTestResult = dotnet test "$TestDir\unit" --configuration $Configuration --verbosity minimal --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Unit tests failed"
        exit $LASTEXITCODE
    }
    
    # Integration tests
    Write-Host "Running integration tests..." -ForegroundColor Yellow
    $integrationTestResult = dotnet test "$TestDir\integration" --configuration $Configuration --verbosity minimal --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Integration tests failed"
        exit $LASTEXITCODE
    }
    
    # Contract tests
    Write-Host "Running contract tests..." -ForegroundColor Yellow
    $contractTestResult = dotnet test "$TestDir\contract" --configuration $Configuration --verbosity minimal --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Contract tests failed"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ All tests passed" -ForegroundColor Green
}

# Restore packages
Write-Host "📦 Restoring packages..." -ForegroundColor Green
dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Package restore failed"
    exit $LASTEXITCODE
}

# Build parameters
$buildParams = @(
    "publish"
    $ProjectFile
    "--configuration", $Configuration
    "--output", $OutputDir
    "--verbosity", ($Verbose ? "detailed" : "minimal")
    "-p:AssemblyVersion=$Version"
    "-p:FileVersion=$Version"
    "-p:InformationalVersion=$InformationalVersion"
    "-p:DebugType=embedded"
)

# Add runtime-specific parameters
if ($Runtime -ne "portable") {
    $buildParams += "--runtime", $Runtime
}

if ($SelfContained -or $Runtime -ne "portable") {
    $buildParams += "--self-contained", "true"
    $buildParams += "-p:IncludeNativeLibrariesForSelfExtract=true"
}

if ($SingleFile -or $Runtime -ne "portable") {
    $buildParams += "-p:PublishSingleFile=true"
    $buildParams += "-p:EnableCompressionInSingleFile=true"
}

# Additional optimizations for Release builds
if ($Configuration -eq "Release") {
    $buildParams += "-p:Optimize=true"
    $buildParams += "-p:TrimUnusedDependencies=true"
    $buildParams += "-p:PublishTrimmed=false"  # Disabled due to WPF compatibility
}

# Build the application
Write-Host "🔨 Building application..." -ForegroundColor Green
Write-Host "Command: dotnet $($buildParams -join ' ')" -ForegroundColor Gray

& dotnet @buildParams
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Build failed"
    exit $LASTEXITCODE
}

# Generate file hash
$exePath = Join-Path $OutputDir "VrcGroupGuardian.exe"
if (Test-Path $exePath) {
    Write-Host "🔐 Generating file hash..." -ForegroundColor Green
    $hash = Get-FileHash -Path $exePath -Algorithm SHA256
    $hashFile = "$exePath.sha256"
    $hash.Hash | Out-File -FilePath $hashFile -Encoding UTF8
    
    Write-Host "✅ Build completed successfully!" -ForegroundColor Green
    Write-Host "📁 Output: $OutputDir" -ForegroundColor Yellow
    Write-Host "📄 Executable: $exePath" -ForegroundColor Yellow
    Write-Host "🔐 SHA256: $hashFile" -ForegroundColor Yellow
    Write-Host "📏 Size: $([math]::Round((Get-Item $exePath).Length / 1MB, 2)) MB" -ForegroundColor Yellow
    
    # Display file information
    $fileInfo = Get-Item $exePath
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
    
    Write-Host ""
    Write-Host "📋 Build Information:" -ForegroundColor Cyan
    Write-Host "  Product Version: $($fileVersion.ProductVersion)" -ForegroundColor White
    Write-Host "  File Version: $($fileVersion.FileVersion)" -ForegroundColor White
    Write-Host "  Build Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
    Write-Host "  SHA256 Hash: $($hash.Hash)" -ForegroundColor White
    
} else {
    Write-Error "❌ Expected executable not found: $exePath"
    exit 1
}

# Performance test (optional)
if ($Configuration -eq "Release" -and !$SkipTests) {
    Write-Host ""
    Write-Host "⚡ Running performance tests..." -ForegroundColor Green
    try {
        dotnet test "$TestDir\performance" --configuration $Configuration --verbosity minimal --no-build
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Performance tests passed" -ForegroundColor Green
        }
    } catch {
        Write-Host "⚠️ Performance tests skipped or failed" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "🎉 Build process completed successfully!" -ForegroundColor Green
Write-Host "Ready for testing or distribution." -ForegroundColor Yellow