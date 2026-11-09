param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'src\XY.SolidWorksPlugin\Properties\AssemblyInfo.cs')), 'AssemblyInformationalVersion\("(?<version>[^"\)]+)"\)')
if (!$versionMatch.Success) { throw 'Assembly informational version was not found.' }
$version = $versionMatch.Groups['version'].Value
$zipPath = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "XY轮盘-源码-v$version.zip"
if ((Test-Path -LiteralPath $zipPath) -and !$Force) { throw "Source archive already exists; use -Force to replace it after verification: $zipPath" }
$stage = Join-Path $repo ('artifacts\source-package-stage-' + $version + '-' + [Guid]::NewGuid().ToString('N'))
$temporaryZip = $zipPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($zipPath)) -Force | Out-Null

$allowedExtensions = @('.jpg','.jpeg','.png','.gif','.ico','.resx','.cs','.csproj','.props','.targets','.sln','.csv','.json','.ps1','.psm1','.cmd','.bat','.py','.md','.txt','.xml','.config')
$deniedExtensions = @('.dll','.pdb','.swp','.trx','.log','.zip','.cache','.tmp','.user')
$deniedDirectoryNames = @('bin','obj','TestResults','artifacts','dist','.git','.reasonix','.workbuddy-ai','tasks','%SystemDrive%','__pycache__')
$roots = @('src','tests','tools','Resources')
$retiredPathPattern = '(?i)(^|[\\/])(Hole[^\\/]*\.cs|PanelGap[^\\/]*\.cs|CutListBom[^\\/]*\.cs|QuoteConfig\.cs)$|(^|[\\/])sw-mcp([\\/]|$)'
$rootFiles = @('LICENSE','NOTICE','Directory.Build.props','BUILD_SOURCE.md','install.ps1','uninstall.ps1','安装.ps1','卸载.ps1','安装.bat','卸载.bat','一键安装.bat','Customize-RadialIcons.cmd','默认配置.json','更新说明.txt','功能介绍说明.txt')
$requiredFiles = @('LICENSE','NOTICE','Directory.Build.props','BUILD_SOURCE.md','src/XY.SolidWorksPlugin/XY.SolidWorksPlugin.csproj','src/XY.SolidWorksPlugin/Properties/AssemblyInfo.cs','tests/XY.SolidWorksPlugin.Tests/XY.SolidWorksPlugin.Tests.csproj','tools/build-source.ps1','tools/package-source.ps1','Resources/contact-wechat.jpg','Resources/contact-douyin.jpg')

function Copy-RelativeFile([string]$Relative) {
    $target = Join-Path $stage $Relative
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $Relative) -Destination $target -Force
}

foreach ($relative in $rootFiles) {
    if (Test-Path -LiteralPath (Join-Path $repo $relative) -PathType Leaf) { Copy-RelativeFile $relative }
}
foreach ($root in $roots) {
    $rootPath = Join-Path $repo $root
    if (!(Test-Path -LiteralPath $rootPath -PathType Container)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $rootPath -Recurse -File) {
        $relative = $file.FullName.Substring($repo.Length).TrimStart([char]0x5C,[char]0x2F)
        if (($relative -split '[\\/]') | Where-Object { $deniedDirectoryNames -contains $_ }) { continue }
        $extension = $file.Extension.ToLowerInvariant()
        if ($deniedExtensions -contains $extension -or $allowedExtensions -notcontains $extension) { continue }
        if ($relative -match $retiredPathPattern) { throw "Retired source/test file must be removed before packaging: $relative" }
        Copy-RelativeFile $relative
    }
}
$verification = "docs\source-cleanup-$version.md"
if (Test-Path -LiteralPath (Join-Path $repo $verification)) { Copy-RelativeFile $verification }

@"
# XY轮盘源码包 $version

本包仅包含轮盘、水印、工序、一键爆炸四大功能及必要依赖、对应测试和构建脚本。用户宏、组合录制和快捷键属于轮盘。
包含微信和抖音联系二维码。许可条款见 LICENSE 和 NOTICE。
不包含编译 DLL/PDB、SWP 宏二进制、用户配置、运行日志、测试结果、历史工作目录或安装载荷。

构建和测试详见 BUILD_SOURCE.md：

    powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build-source.ps1 -RunTests

该脚本只读检测本机 SOLIDWORKS 安装目录，也可显式指定 -SolidWorksInteropDirectory。
源码包不代表已部署或已通过宿主验收；本版本实际验证范围见 $verification（若随包提供）。
安装脚本是模板，需先完成构建并准备匹配版本的安装载荷。
SOURCE_PACKAGE_MANIFEST.txt 记录包内文件 SHA256；ZIP 生成后逐文件回读校验。
"@ | Set-Content -LiteralPath (Join-Path $stage 'SOURCE_PACKAGE_README.md') -Encoding UTF8

$manifest = [Collections.Generic.List[string]]::new()
$manifest.Add("version=$version")
$manifest.Add("generated=$([DateTime]::UtcNow.ToString('o'))")
foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName) {
    $relative = $file.FullName.Substring($stage.Length).TrimStart([char]0x5C,[char]0x2F).Replace('\','/')
    $manifest.Add((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash + '  ' + $relative)
}
$manifest | Set-Content -LiteralPath (Join-Path $stage 'SOURCE_PACKAGE_MANIFEST.txt') -Encoding UTF8

$expected = @{}
foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
    $relative = $file.FullName.Substring($stage.Length).TrimStart([char]0x5C,[char]0x2F).Replace('\','/')
    if ($expected.ContainsKey($relative)) { throw "Duplicate source path: $relative" }
    $expected[$relative] = @{ Length = $file.Length; Hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
foreach ($required in $requiredFiles) { if (!$expected.ContainsKey($required)) { throw "Required source file missing: $required" } }

try {
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false, [Text.Encoding]::UTF8)
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
    $seen = @{}
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or $name -match '(^|/)\.\.?(/|$)') { throw "Unsafe ZIP entry: $name" }
            if (($name -split '/') | Where-Object { $deniedDirectoryNames -contains $_ }) { throw "Denied directory in ZIP: $name" }
            if ($deniedExtensions -contains ([IO.Path]::GetExtension($name).ToLowerInvariant())) { throw "Denied extension in ZIP: $name" }
            if ($name -match '(?i)(^|/)(.*(alipay|donation|purchase).*(jpg|jpeg|png|gif)|error\.log)$') { throw "Removed asset in ZIP: $name" }
            if ($seen.ContainsKey($name) -or !$expected.ContainsKey($name)) { throw "Duplicate or unexpected ZIP entry: $name" }
            if ($entry.Length -ne $expected[$name].Length) { throw "ZIP length mismatch: $name" }
            $stream = $entry.Open()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() }
            if ($hash -ne $expected[$name].Hash) { throw "ZIP SHA256 mismatch: $name" }
            $seen[$name] = $true
        }
        if ($seen.Count -ne $expected.Count) { throw 'ZIP file count does not match the staged source.' }
    } finally { $sha.Dispose(); $archive.Dispose() }

    if (Test-Path -LiteralPath $zipPath) { [IO.File]::Replace($temporaryZip, $zipPath, $null) }
    else { [IO.File]::Move($temporaryZip, $zipPath) }
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    ($zipHash + '  ' + [IO.Path]::GetFileName($zipPath)) | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding UTF8
    [ordered]@{ Version = $version; Path = $zipPath; Files = $seen.Count; Sha256 = $zipHash; Stage = $stage; VerifiedUtc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath ($zipPath + '.verification.json') -Encoding UTF8
    Write-Output "Created $zipPath ($($seen.Count) files, every entry verified)"
    Write-Output "SHA256 $zipHash"
} finally {
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
}