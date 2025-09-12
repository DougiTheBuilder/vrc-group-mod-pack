# Installer Creation Script for VRC Group Guardian
# This script creates a Windows installer using NSIS

param(
    [Parameter(Mandatory=$true, HelpMessage="Path to the signed executable")]
    [string]$ExecutablePath,
    
    [Parameter(HelpMessage="Version number for the installer")]
    [string]$Version = "1.0.0",
    
    [Parameter(HelpMessage="Output directory for the installer")]
    [string]$OutputDir = ".\output",
    
    [Parameter(HelpMessage="Path to NSIS installation")]
    [string]$NsisPath = "${env:ProgramFiles(x86)}\NSIS",
    
    [Parameter(HelpMessage="Sign the installer after creation")]
    [switch]$SignInstaller,
    
    [Parameter(HelpMessage="Path to code signing certificate (required if SignInstaller is used)")]
    [string]$CertificatePath,
    
    [Parameter(HelpMessage="Certificate password (will prompt if not provided)")]
    [SecureString]$CertificatePassword
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "📦 VRC Group Guardian Installer Creation" -ForegroundColor Cyan
Write-Host ""

# Validate inputs
if (!(Test-Path $ExecutablePath)) {
    Write-Error "❌ Executable not found: $ExecutablePath"
    exit 1
}

if (!(Test-Path $NsisPath)) {
    Write-Error "❌ NSIS not found at: $NsisPath. Please install NSIS or specify correct path."
    exit 1
}

$makensis = Join-Path $NsisPath "makensis.exe"
if (!(Test-Path $makensis)) {
    Write-Error "❌ makensis.exe not found at: $makensis"
    exit 1
}

Write-Host "✅ Executable: $ExecutablePath" -ForegroundColor Green
Write-Host "✅ NSIS: $makensis" -ForegroundColor Green
Write-Host "✅ Version: $Version" -ForegroundColor Green

# Create output directory
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Copy required files to staging area
$stagingDir = Join-Path $OutputDir "staging"
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Write-Host ""
Write-Host "📋 Preparing installation files..." -ForegroundColor Green

# Copy executable
Copy-Item $ExecutablePath -Destination $stagingDir
$executableName = Split-Path $ExecutablePath -Leaf
Write-Host "✅ Copied: $executableName"

# Copy hash file if it exists
$hashFile = "$ExecutablePath.sha256"
if (Test-Path $hashFile) {
    Copy-Item $hashFile -Destination $stagingDir
    Write-Host "✅ Copied: $(Split-Path $hashFile -Leaf)"
}

# Copy documentation files
$docFiles = @("README.md", "LICENSE.txt", "CHANGELOG.md")
foreach ($file in $docFiles) {
    $sourcePath = Join-Path $ProjectRoot $file
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath -Destination $stagingDir
        Write-Host "✅ Copied: $file"
    }
}

# Copy quickstart guide if it exists
$quickstartPath = Join-Path $ProjectRoot "specs\001-vrc-group-guardian\quickstart.md"
if (Test-Path $quickstartPath) {
    Copy-Item $quickstartPath -Destination (Join-Path $stagingDir "QuickStart.md")
    Write-Host "✅ Copied: QuickStart.md"
}

# Create NSIS installer script
Write-Host ""
Write-Host "📝 Creating installer script..." -ForegroundColor Green

$installerScript = @"
!define APP_NAME "VRC Group Guardian"
!define APP_VERSION "$Version"
!define APP_PUBLISHER "VRC Group Guardian Project"
!define APP_URL "https://github.com/your-username/vrc-group-mod-pack"
!define APP_EXE "$executableName"
!define APP_GUID "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

# Installer properties
Name "`${APP_NAME} `${APP_VERSION}"
OutFile "$OutputDir\VrcGroupGuardian-Setup-`${APP_VERSION}.exe"
InstallDir "`$PROGRAMFILES64\VrcGroupGuardian"
RequestExecutionLevel admin
BrandingText "`${APP_NAME} v`${APP_VERSION}"

# Version information
VIProductVersion "$Version.0"
VIAddVersionKey "ProductName" "`${APP_NAME}"
VIAddVersionKey "ProductVersion" "`${APP_VERSION}"
VIAddVersionKey "CompanyName" "`${APP_PUBLISHER}"
VIAddVersionKey "FileDescription" "`${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "`${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "© 2025 `${APP_PUBLISHER}"

# Include Modern UI
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

# Modern UI Configuration
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "`${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT "This will install `${APP_NAME} v`${APP_VERSION} on your computer.`$\r`$\n`$\r`$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN "`$INSTDIR\`${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch `${APP_NAME}"
!define MUI_FINISHPAGE_LINK "Visit the project website"
!define MUI_FINISHPAGE_LINK_LOCATION "`${APP_URL}"

# Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "$stagingDir\LICENSE.txt"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

# Languages
!insertmacro MUI_LANGUAGE "English"

# Installation sections
Section "`${APP_NAME} (required)" SEC01
  SectionIn RO
  
  # System requirements check
  `${If} `${IsWin10}
    # Windows 10 or later
  `${ElseIf} `${IsWin2008R2}
    # Windows 7/2008R2 or later  
  `${Else}
    MessageBox MB_OK|MB_ICONSTOP "This application requires Windows 10 or later."
    Abort
  `${EndIf}
  
  # Architecture check
  `${If} `${RunningX64}
    # 64-bit system
  `${Else}
    # Check if we have 32-bit executable
    IfFileExists "$stagingDir\*win-x86*" +2
      MessageBox MB_OK|MB_ICONSTOP "This application requires a 64-bit version of Windows."
      Abort
  `${EndIf}
  
  # Install files
  SetOutPath "`$INSTDIR"
  File "$stagingDir\`${APP_EXE}"
  File /nonfatal "$stagingDir\`${APP_EXE}.sha256"
  File /nonfatal "$stagingDir\README.md"
  File /nonfatal "$stagingDir\LICENSE.txt"
  File /nonfatal "$stagingDir\CHANGELOG.md"
  File /nonfatal "$stagingDir\QuickStart.md"
  
  # Create uninstaller
  WriteUninstaller "`$INSTDIR\uninstall.exe"
  
  # Registry entries for Add/Remove Programs
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "DisplayName" "`${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "DisplayVersion" "`${APP_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "Publisher" "`${APP_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "URLInfoAbout" "`${APP_URL}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "UninstallString" "`$INSTDIR\uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "DisplayIcon" "`$INSTDIR\`${APP_EXE}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "InstallLocation" "`$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "NoRepair" 1
  
  # Calculate installed size
  `${GetSize} "`$INSTDIR" "/S=0K" `$0 `$1 `$2
  IntFmt `$0 "0x%08X" `$0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "EstimatedSize" "`$0"
SectionEnd

Section "Desktop Shortcut" SEC02
  CreateShortCut "`$DESKTOP\`${APP_NAME}.lnk" "`$INSTDIR\`${APP_EXE}"
SectionEnd

Section "Start Menu Shortcuts" SEC03
  CreateDirectory "`$SMPROGRAMS\`${APP_NAME}"
  CreateShortCut "`$SMPROGRAMS\`${APP_NAME}\`${APP_NAME}.lnk" "`$INSTDIR\`${APP_EXE}"
  CreateShortCut "`$SMPROGRAMS\`${APP_NAME}\Uninstall.lnk" "`$INSTDIR\uninstall.exe"
  
  # Documentation shortcuts
  IfFileExists "`$INSTDIR\README.md" 0 +2
    CreateShortCut "`$SMPROGRAMS\`${APP_NAME}\README.lnk" "`$INSTDIR\README.md"
  IfFileExists "`$INSTDIR\QuickStart.md" 0 +2  
    CreateShortCut "`$SMPROGRAMS\`${APP_NAME}\Quick Start Guide.lnk" "`$INSTDIR\QuickStart.md"
SectionEnd

# Section descriptions
LangString DESC_SEC01 `${LANG_ENGLISH} "The main application files. This section is required."
LangString DESC_SEC02 `${LANG_ENGLISH} "Create a shortcut on the desktop for easy access."
LangString DESC_SEC03 `${LANG_ENGLISH} "Create shortcuts in the Start Menu."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT `${SEC01} `$(DESC_SEC01)
  !insertmacro MUI_DESCRIPTION_TEXT `${SEC02} `$(DESC_SEC02)
  !insertmacro MUI_DESCRIPTION_TEXT `${SEC03} `$(DESC_SEC03)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

# Uninstaller
Section "Uninstall"
  # Remove files
  Delete "`$INSTDIR\`${APP_EXE}"
  Delete "`$INSTDIR\`${APP_EXE}.sha256"
  Delete "`$INSTDIR\README.md"
  Delete "`$INSTDIR\LICENSE.txt"
  Delete "`$INSTDIR\CHANGELOG.md"
  Delete "`$INSTDIR\QuickStart.md"
  Delete "`$INSTDIR\uninstall.exe"
  
  # Remove shortcuts
  Delete "`$DESKTOP\`${APP_NAME}.lnk"
  Delete "`$SMPROGRAMS\`${APP_NAME}\`${APP_NAME}.lnk"
  Delete "`$SMPROGRAMS\`${APP_NAME}\Uninstall.lnk"
  Delete "`$SMPROGRAMS\`${APP_NAME}\README.lnk"
  Delete "`$SMPROGRAMS\`${APP_NAME}\Quick Start Guide.lnk"
  RMDir "`$SMPROGRAMS\`${APP_NAME}"
  
  # Remove installation directory
  RMDir "`$INSTDIR"
  
  # Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}"
  
  # Ask if user wants to remove user data
  MessageBox MB_YESNO|MB_ICONQUESTION "Do you want to remove all user settings and data?" IDNO +3
    RMDir /r "`$APPDATA\VrcGroupGuardian"
    MessageBox MB_OK "User settings and data have been removed."
SectionEnd

# Functions
Function .onInit
  # Check if already installed
  ReadRegStr `$R0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\`${APP_GUID}" "UninstallString"
  StrCmp `$R0 "" done
  
  MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "`${APP_NAME} is already installed. `$\n`$\nClick OK to remove the previous version or Cancel to cancel this upgrade." IDOK uninst
  Abort
  
uninst:
  ClearErrors
  ExecWait '`$R0 _?=`$INSTDIR'
  
done:
FunctionEnd
"@

$scriptPath = Join-Path $OutputDir "installer.nsi"
$installerScript | Out-File -FilePath $scriptPath -Encoding UTF8

Write-Host "✅ Installer script created: $scriptPath"

# Build the installer
Write-Host ""
Write-Host "🔨 Building installer..." -ForegroundColor Green

$installerOutput = & $makensis $scriptPath 2>&1
$nsisResult = $LASTEXITCODE

if ($nsisResult -eq 0) {
    Write-Host "✅ Installer built successfully!" -ForegroundColor Green
    
    $installerPath = Join-Path $OutputDir "VrcGroupGuardian-Setup-$Version.exe"
    if (Test-Path $installerPath) {
        Write-Host "📦 Installer: $installerPath" -ForegroundColor Yellow
        Write-Host "📏 Size: $([math]::Round((Get-Item $installerPath).Length / 1MB, 2)) MB" -ForegroundColor Yellow
        
        # Generate hash
        Write-Host ""
        Write-Host "🔐 Generating installer hash..." -ForegroundColor Green
        $hash = Get-FileHash -Path $installerPath -Algorithm SHA256
        $hashFile = "$installerPath.sha256"
        $hash.Hash | Out-File -FilePath $hashFile -Encoding UTF8
        Write-Host "✅ SHA256: $($hash.Hash)" -ForegroundColor Yellow
        Write-Host "📄 Hash file: $hashFile" -ForegroundColor Yellow
        
        # Sign installer if requested
        if ($SignInstaller) {
            if (-not $CertificatePath) {
                Write-Error "❌ Certificate path required for signing"
                exit 1
            }
            
            Write-Host ""
            Write-Host "🔐 Signing installer..." -ForegroundColor Green
            
            $signScript = Join-Path (Split-Path $PSScriptRoot) "build\sign-executable.ps1"
            $signParams = @{
                ExecutablePath = $installerPath
                CertificatePath = $CertificatePath
            }
            
            if ($CertificatePassword) {
                $signParams.CertificatePassword = $CertificatePassword
            }
            
            & $signScript @signParams
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ Installer signed successfully!" -ForegroundColor Green
            } else {
                Write-Warning "⚠️ Installer signing failed"
            }
        }
        
        Write-Host ""
        Write-Host "🎉 Installer creation completed successfully!" -ForegroundColor Green
        Write-Host "The installer is ready for distribution." -ForegroundColor Yellow
        
    } else {
        Write-Error "❌ Installer file not found at expected location: $installerPath"
        exit 1
    }
} else {
    Write-Error "❌ Installer build failed with exit code $nsisResult"
    Write-Host "NSIS Output:" -ForegroundColor Red
    Write-Host $installerOutput -ForegroundColor Red
    exit $nsisResult
}

# Clean up staging directory
Write-Host ""
Write-Host "🧹 Cleaning up..." -ForegroundColor Green
Remove-Item $stagingDir -Recurse -Force
Remove-Item $scriptPath -Force

Write-Host "✅ Cleanup completed" -ForegroundColor Green