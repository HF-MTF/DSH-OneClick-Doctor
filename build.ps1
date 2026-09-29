# DSH 一键诊断 —— 编译 + 部署 + 自检
$src = 'E:\DeepSeekHarness\doctor-src'
$out = Join-Path $src 'build'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Remove-Item "$out\DSH一键诊断.exe" -Force -ErrorAction SilentlyContinue
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /optimize+ /out:"$out\DSH一键诊断.exe" /win32icon:"$src\app.ico" /win32manifest:"$src\app.manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$src\DshDoctor.cs"
if ($LASTEXITCODE -ne 0) { Write-Output '编译失败，未部署'; exit 1 }
Copy-Item "$out\DSH一键诊断.exe" 'E:\DeepSeekHarness\DSH一键诊断.exe' -Force
# 同步一份到 dist\：随源码一起发布，下载解压即可运行
$dist = Join-Path $src 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item "$out\DSH一键诊断.exe" (Join-Path $dist 'DSH一键诊断.exe') -Force
Write-Output ('已部署 ' + [math]::Round((Get-Item 'E:\DeepSeekHarness\DSH一键诊断.exe').Length/1KB) + ' KB')
Write-Output '--- 无界面自检 ---'
& 'E:\DeepSeekHarness\DSH一键诊断.exe' --auto
Write-Output ('自检退出码=' + $LASTEXITCODE + '  (0=全通过 1=有警告 2=有错误)')
Write-Output ('报告: E:\DeepSeekHarness\DSH诊断报告.txt')