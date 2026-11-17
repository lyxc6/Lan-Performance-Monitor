$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "找不到 .NET Framework 4.x 编译器：$csc"
}

$framework = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$sources = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }
$deps = Get-ChildItem "$root\tools\lhm096\*.dll" | Sort-Object Name

if ($deps.Count -lt 5) {
    throw "缺少 tools\lhm096 下的 LibreHardwareMonitor 依赖。"
}
if (-not (Test-Path "$root\tools\PresentMon.exe")) {
    throw "缺少 tools\PresentMon.exe。"
}

if (-not (Test-Path "$root\dist")) {
    New-Item -ItemType Directory -Path "$root\dist" | Out-Null
}

$resources = @(
    "/resource:$root\web\index.html,res.web.index.html",
    "/resource:$root\web\apple-touch-icon.png,res.web.apple-touch-icon.png",
    "/resource:$root\assets\app.ico,res.app.ico",
    "/resource:$root\tools\PresentMon.exe,res.tool.PresentMon.exe"
)
foreach ($dep in $deps) {
    $resources += "/resource:$($dep.FullName),res.dep.$($dep.Name)"
}

$references = @(
    "/r:$framework\System.dll",
    "/r:$framework\System.Core.dll",
    "/r:$framework\System.Drawing.dll",
    "/r:$framework\System.Windows.Forms.dll",
    "/r:$framework\System.Management.dll",
    "/r:$root\tools\lhm096\LibreHardwareMonitorLib.dll"
)

$common = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/debug-",
    "/win32icon:$root\assets\app.ico"
) + $references + $resources

Write-Host "Compiling test build (asInvoker, for local HTTP/UI verification)..."
& $csc $common "/out:$root\dist\LanPerfMonTest.exe" $sources
if ($LASTEXITCODE -ne 0) {
    throw "测试版编译失败，退出码 $LASTEXITCODE"
}

Write-Host "Compiling final build (requireAdministrator)..."
& $csc $common "/win32manifest:$root\app.manifest" "/out:$root\dist\LanPerfMon.exe" $sources
if ($LASTEXITCODE -ne 0) {
    throw "正式版编译失败，退出码 $LASTEXITCODE"
}

Get-Item "$root\dist\LanPerfMonTest.exe", "$root\dist\LanPerfMon.exe" |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
