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

# 用重定向捕获窗口子系统进程的输出（它是 winexe，直接调用看不到 Console 输出）
function Invoke-Quiet([string]$argList) {
    $o = Join-Path $env:TEMP ('dshdoc_' + [guid]::NewGuid().ToString('N') + '.out')
    $e = Join-Path $env:TEMP ('dshdoc_' + [guid]::NewGuid().ToString('N') + '.err')
    $p = Start-Process -FilePath $exe -ArgumentList $argList -Wait -PassThru -NoNewWindow -RedirectStandardOutput $o -RedirectStandardError $e
    if (Test-Path $o) { Get-Content $o -Encoding UTF8 | ForEach-Object { if ($_ -ne '') { Write-Host $_ } } }
    if (Test-Path $e) { Get-Content $e -Encoding UTF8 | ForEach-Object { if ($_ -ne '') { Write-Host $_ -ForegroundColor Yellow } } }
    Remove-Item $o,$e -Force -ErrorAction SilentlyContinue
    return $p.ExitCode
}

Write-Host '--- 自动探测并部署到 DSH 目录 ---'
$dc = Invoke-Quiet '--deploy'
if ($dc -ne 0) {
    Write-Host '（没找到 DSH 目录也没关系：这个 exe 放在任何位置都能正常用）' -ForegroundColor Yellow
}

Write-Host '--- 无界面自检 ---'
$sc = Invoke-Quiet '--auto'
Write-Host ('自检退出码=' + $sc + '  (0=全通过 1=有警告 2=有错误)')
