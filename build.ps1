# DSH 一键诊断 —— 编译脚本
# 不依赖任何绝对路径，整个文件夹可以随便移动、放到任何盘任何目录
# 编译产物直接放在本文件夹根目录，双击即用

$ErrorActionPreference = 'Continue'
$src  = $PSScriptRoot
$tmp  = Join-Path $src 'build'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# 找 csc.exe：64 位优先，32 位系统自动回退
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) {
    Write-Host '[X] 找不到 csc.exe —— 请确认系统已安装 .NET Framework 4.x（Win10/11 自带）' -ForegroundColor Red
    exit 1
}

$exe = Join-Path $src 'DSH一键诊断.exe'
& $csc /nologo /target:winexe /optimize+ "/out:$exe" "/win32icon:$src\app.ico" "/win32manifest:$src\app.manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$src\DshDoctor.cs"
if ($LASTEXITCODE -ne 0) {
    Write-Host '[X] 编译失败' -ForegroundColor Red
    exit 1
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ('[OK] 已生成 ' + [math]::Round((Get-Item $exe).Length / 1KB) + ' KB -> DSH一键诊断.exe（就在本文件夹里）') -ForegroundColor Green

# 如果这个文件夹的上一层看起来就是 DSH 安装目录（含 dsh / node），顺手也放一份过去
$parent = Split-Path $src -Parent
if ($parent -and ((Test-Path (Join-Path $parent 'dsh')) -or (Test-Path (Join-Path $parent 'node')))) {
    Copy-Item $exe (Join-Path $parent 'DSH一键诊断.exe') -Force
    Write-Host ('[OK] 已同时部署到 ' + $parent) -ForegroundColor Green
}

Write-Host '--- 无界面自检 ---'
& $exe --auto
Write-Host ('自检退出码=' + $LASTEXITCODE + '  (0=全通过 1=有警告 2=有错误)')
$rep = Join-Path $src 'DSH诊断报告.txt'
if (Test-Path $rep) { Write-Host ('报告: ' + $rep) }
