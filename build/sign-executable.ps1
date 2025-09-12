# Code Signing Script for VRC Group Guardian
# This script signs the built executable with a code signing certificate

param(
    [Parameter(Mandatory=$true, HelpMessage="Path to the executable to sign")]
    [string]$ExecutablePath,
    
    [Parameter(Mandatory=$true, HelpMessage="Path to the code signing certificate (.pfx file)")]
    [string]$CertificatePath,
    
    [Parameter(HelpMessage="Password for the certificate (if not provided, will prompt)")]
    [SecureString]$CertificatePassword,
    
    [Parameter(HelpMessage="Timestamp server URL")]
    [string]$TimestampServer = "http://timestamp.digicert.com",
    
    [Parameter(HelpMessage="Skip certificate validation")]
    [switch]$SkipValidation,
    
    [Parameter(HelpMessage="Verbose output")]
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

Write-Host "🔒 VRC Group Guardian Code Signing" -ForegroundColor Cyan
Write-Host ""

# Validate inputs
if (!(Test-Path $ExecutablePath)) {
    Write-Error "❌ Executable not found: $ExecutablePath"
    exit 1
}

if (!(Test-Path $CertificatePath)) {
    Write-Error "❌ Certificate not found: $CertificatePath"
    exit 1
}

Write-Host "✅ Executable: $ExecutablePath" -ForegroundColor Green
Write-Host "✅ Certificate: $CertificatePath" -ForegroundColor Green

# Get certificate password if not provided
if (-not $CertificatePassword) {
    $CertificatePassword = Read-Host -Prompt "Enter certificate password" -AsSecureString
}

# Find SignTool
$signToolPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
    "${env:ProgramFiles}\Microsoft SDKs\Windows\v7.1A\Bin\signtool.exe"
)

$signTool = $null
foreach ($path in $signToolPaths) {
    if (Test-Path $path) {
        $signTool = $path
        break
    }
}

if (-not $signTool) {
    Write-Error "❌ SignTool not found. Please install Windows SDK."
    exit 1
}

Write-Host "✅ SignTool: $signTool" -ForegroundColor Green

# Prepare certificate password for command line
$passwordPlainText = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))

try {
    # Sign the executable
    Write-Host ""
    Write-Host "🔐 Signing executable..." -ForegroundColor Green
    
    $signArgs = @(
        "sign"
        "/f", $CertificatePath
        "/p", $passwordPlainText
        "/t", $TimestampServer
        "/fd", "SHA256"
        "/td", "SHA256"
        "/d", "VRC Group Guardian"
        "/du", "https://github.com/your-username/vrc-group-mod-pack"
    )
    
    if ($Verbose) {
        $signArgs += "/v"
    }
    
    $signArgs += $ExecutablePath
    
    # Execute signing
    & $signTool @signArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Code signing failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ Executable signed successfully!" -ForegroundColor Green
    
    # Verify signature
    if (-not $SkipValidation) {
        Write-Host ""
        Write-Host "🔍 Verifying signature..." -ForegroundColor Green
        
        & $signTool "verify" "/pa" $ExecutablePath
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Signature verification passed!" -ForegroundColor Green
        } else {
            Write-Warning "⚠️ Signature verification failed, but file was signed"
        }
    }
    
    # Generate new hash after signing
    Write-Host ""
    Write-Host "🔐 Generating new file hash..." -ForegroundColor Green
    $hash = Get-FileHash -Path $ExecutablePath -Algorithm SHA256
    $hashFile = "$ExecutablePath.sha256"
    $hash.Hash | Out-File -FilePath $hashFile -Encoding UTF8
    
    Write-Host "✅ New SHA256 hash: $($hash.Hash)" -ForegroundColor Yellow
    Write-Host "📄 Hash saved to: $hashFile" -ForegroundColor Yellow
    
    # Display certificate information
    Write-Host ""
    Write-Host "📋 Signature Information:" -ForegroundColor Cyan
    
    try {
        $cert = Get-AuthenticodeSignature -FilePath $ExecutablePath
        if ($cert.Status -eq "Valid") {
            Write-Host "  Status: ✅ Valid" -ForegroundColor Green
            Write-Host "  Subject: $($cert.SignerCertificate.Subject)" -ForegroundColor White
            Write-Host "  Issuer: $($cert.SignerCertificate.Issuer)" -ForegroundColor White
            Write-Host "  Valid From: $($cert.SignerCertificate.NotBefore)" -ForegroundColor White
            Write-Host "  Valid To: $($cert.SignerCertificate.NotAfter)" -ForegroundColor White
            Write-Host "  Thumbprint: $($cert.SignerCertificate.Thumbprint)" -ForegroundColor White
            
            if ($cert.TimeStamperCertificate) {
                Write-Host "  Timestamp: ✅ Present" -ForegroundColor Green
                Write-Host "  Timestamp Authority: $($cert.TimeStamperCertificate.Subject)" -ForegroundColor White
            } else {
                Write-Host "  Timestamp: ❌ Missing" -ForegroundColor Red
            }
        } else {
            Write-Host "  Status: ❌ $($cert.Status)" -ForegroundColor Red
            Write-Host "  Status Message: $($cert.StatusMessage)" -ForegroundColor Red
        }
    } catch {
        Write-Warning "⚠️ Could not retrieve detailed signature information: $($_.Exception.Message)"
    }
    
} finally {
    # Clear password from memory
    $passwordPlainText = $null
    [System.GC]::Collect()
}

Write-Host ""
Write-Host "🎉 Code signing completed successfully!" -ForegroundColor Green
Write-Host "The executable is now digitally signed and ready for distribution." -ForegroundColor Yellow