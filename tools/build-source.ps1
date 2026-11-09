param(
    [string]$SolidWorksInteropDirectory,
    [switch]$RunTests,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$required = @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll', 'SolidWorks.Interop.swcommands.dll', 'SolidWorks.Interop.swpublished.dll')

function Test-InteropDirectory([string]$Directory) {
    if ([string]::IsNullOrWhiteSpace($Directory)) { return $false }
    foreach ($name in $required) { if (!(Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf)) { return $false } }
    return $true
}

if ($SolidWorksInteropDirectory) {
    if (!(Test-InteropDirectory $SolidWorksInteropDirectory)) { throw 'The specified directory must contain all four SOLIDWORKS interop DLLs.' }
} else {
    $candidates = @($env:SOLIDWORKS_INTEROP_DIR)
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $root = $machine.OpenSubKey('SOFTWARE\SolidWorks')
            if ($null -eq $root) { continue }
            try {
                foreach ($name in $root.GetSubKeyNames() | Where-Object { $_ -match '^SOLIDWORKS \d{4}$' } | Sort-Object -Descending) {
                    $setup = $root.OpenSubKey($name + '\Setup')
                    if ($null -eq $setup) { continue }
                    try { $candidates += [string]$setup.GetValue('SolidWorks Folder') } finally { $setup.Dispose() }
                }
            } finally { $root.Dispose() }
        } finally { $machine.Dispose() }
    }
    $candidates += (Join-Path $env:ProgramW6432 'SOLIDWORKS Corp\SOLIDWORKS')
    $SolidWorksInteropDirectory = $candidates | Where-Object { Test-InteropDirectory $_ } | Select-Object -First 1
    if (!$SolidWorksInteropDirectory) { throw 'SOLIDWORKS was not found. Pass -SolidWorksInteropDirectory or set SOLIDWORKS_INTEROP_DIR.' }
}

$interop = [IO.Path]::GetFullPath($SolidWorksInteropDirectory).TrimEnd([char]0x5C, [char]0x2F)
Write-Output "SOLIDWORKS interop: $interop"
$project = Join-Path $repo 'src\XY.SolidWorksPlugin\XY.SolidWorksPlugin.csproj'
& dotnet build $project -c Release '-p:Platform=x64' "-p:SolidWorksInteropDirectory=$interop" --nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed ($LASTEXITCODE)." }

if ($RunTests) {
    if (!$ResultsDirectory) { $ResultsDirectory = Join-Path $repo 'artifacts\test-results' }
    $tests = Join-Path $repo 'tests\XY.SolidWorksPlugin.Tests\XY.SolidWorksPlugin.Tests.csproj'
    & dotnet test $tests -c Release '-p:Platform=x64' "-p:SolidWorksInteropDirectory=$interop" --logger 'trx;LogFileName=source-tests.trx' --results-directory $ResultsDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
}
