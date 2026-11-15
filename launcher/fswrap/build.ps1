# Builds FFX&X-2_LAUNCHER.exe (standalone launcher, x86, .NET Framework 4) from src\*.cs with the csc.exe that ships
# with Windows; no SDK or NuGet needed. The result runs on Windows and under Wine Mono (Proton / Steam Deck).
# launcher.ico (the icon of the original launcher, extracted from its resources) becomes the exe icon if present.
#   powershell -File build.ps1 [-Deploy <game dir>]
# -Deploy replaces <game dir>\FFX&X-2_LAUNCHER.exe. The original is first copied to FFX&X-2_LAUNCHER_original.exe;
# an existing backup is never overwritten, and nothing is replaced while the target is signed and no backup exists.
# legacy\FFXLauncherFS_wrapper.cs is the earlier wrapper around the original launcher (not built).
param([string]$Deploy = "")
$ErrorActionPreference = "Stop"
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$out = Join-Path $PSScriptRoot "bin"
$name = "FFX&X-2_LAUNCHER.exe"
$icon = Join-Path $PSScriptRoot "launcher.ico"
New-Item -ItemType Directory -Force $out | Out-Null
$src = @(Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs | ForEach-Object { $_.FullName })
# the 60 fps patcher inside the launcher: ..\..\fps60exe\Patcher.cs + the patch image built by fps60exe\build.py (docs\FPS60_EXE.md).
# Without the image the launcher is built without it and writes no 60 fps EXEs (Patcher.Available = false).
$f60 = Join-Path $PSScriptRoot "..\..\fps60exe"; $resArg = @()
$src += (Join-Path $f60 "Patcher.cs")
if (Test-Path (Join-Path $f60 "obj\f60.img")) { $resArg = @("/resource:$f60\obj\f60.img,f60.img", "/resource:$f60\ffx_enh.ini,ffx_enh.ini") }
else { Write-Host "fps60exe\obj\f60.img not found (python fps60exe\build.py): launcher built without the 60 fps patcher" -ForegroundColor Yellow }
$iconArg = @(); if (Test-Path -LiteralPath $icon) { $iconArg = @("/win32icon:$icon") }
& $csc /nologo /target:winexe /platform:x86 /optimize+ /warn:4 "/out:$out\$name" @iconArg @resArg `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Core.dll $src
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
if ($Deploy) {
    $target = Join-Path $Deploy $name
    $backup = Join-Path $Deploy "FFX&X-2_LAUNCHER_original.exe"
    if ((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $backup)) {
        if ((Get-AuthenticodeSignature -LiteralPath $target).Status -ne "Valid") { throw "no backup of the original launcher in $Deploy and the target is not the signed original" }
        Copy-Item -LiteralPath $target -Destination $backup; "original backed up to $backup"
    }
    Copy-Item -LiteralPath "$out\$name" -Destination $target -Force; "deployed to $target"
}
