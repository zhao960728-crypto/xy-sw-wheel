$ErrorActionPreference = "Stop"
$Guid = "{8D97B869-0F3D-4F13-901A-0A63D9D13A12}"
$PackageRoot = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$PluginFolderName = -join ([char[]](0x63D2, 0x4EF6, 0x6587, 0x4EF6))
$PackageAddin = Join-Path (Join-Path $PackageRoot $PluginFolderName) "XY.SolidWorksPlugin.SwAddin.dll"
$InstallRoot = "$env:ProgramData\XY\XYRadialMenu.SW"
$RegAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

Add-Type -AssemblyName System.Windows.Forms

function Test-Administrator {
	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-RegisteredAddinPath {
	$key = "Registry::HKEY_CLASSES_ROOT\CLSID\$Guid\InprocServer32"
	if (!(Test-Path -LiteralPath $key)) { return $null }
	$codeBase = [string](Get-ItemProperty -LiteralPath $key -Name CodeBase -ErrorAction SilentlyContinue).CodeBase
	if ([string]::IsNullOrWhiteSpace($codeBase)) { return $null }
	try { return [IO.Path]::GetFullPath(([Uri]$codeBase).LocalPath) }
	catch { return [IO.Path]::GetFullPath($codeBase) }
}

function Get-SolidWorksApplication {
	try { return [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application") }
	catch { return $null }
}

function Get-UnsavedDocuments($sw) {
	$unsaved = New-Object System.Collections.Generic.List[string]
	if ($null -eq $sw) { return $unsaved }
	try {
		$document = $sw.GetFirstDocument()
		while ($null -ne $document) {
			if ([bool]$document.GetSaveFlag()) { $unsaved.Add([string]$document.GetTitle()) }
			$document = $document.GetNext()
		}
	} catch {
		Write-Warning "Unable to read SOLIDWORKS document save state: $($_.Exception.Message)"
		return $null
	}
	return $unsaved
}

function Request-SolidWorksClose($sw) {
	Write-Host "Closing SOLIDWORKS safely..."
	try {
		if ($null -ne $sw) { $sw.ExitApp() | Out-Null }
	} catch { }
	Start-Sleep -Milliseconds 500
	foreach ($process in @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)) {
		try { if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.CloseMainWindow() | Out-Null } } catch { }
	}
}

function Close-SolidWorksForUninstall {
	if (@(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue).Count -eq 0) { return }
	$sw = Get-SolidWorksApplication
	if ($null -ne $sw) {
		while ($true) {
			$unsaved = Get-UnsavedDocuments $sw
			if ($null -eq $unsaved -or $unsaved.Count -eq 0) { break }
			$message = "SOLIDWORKS has unsaved documents:`r`n`r`n" + ($unsaved -join "`r`n") + "`r`n`r`nSave them in SOLIDWORKS, then click OK. The uninstaller will close SOLIDWORKS safely and continue."
			[System.Windows.Forms.MessageBox]::Show($message, "XY Plug-in Uninstaller", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
			Start-Sleep -Milliseconds 500
		}
	} else {
		$message = "SOLIDWORKS is running, but its document save state could not be read. Save your work first, then click OK. The uninstaller will request a normal SOLIDWORKS shutdown."
		[System.Windows.Forms.MessageBox]::Show($message, "XY Plug-in Uninstaller", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
	}
	Request-SolidWorksClose $sw
	$deadline = (Get-Date).AddSeconds(90)
	while ((Get-Date) -lt $deadline) {
		$processes = @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)
		if ($processes.Count -eq 0) { return }
		foreach ($process in $processes) {
			try { if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $process.CloseMainWindow() | Out-Null } } catch { }
		}
		Start-Sleep -Milliseconds 500
	}
	throw "SOLIDWORKS did not close. Handle any save prompt, close SOLIDWORKS, and run uninstall again."
}

if (!(Test-Administrator)) {
	$args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
	Start-Process -FilePath (Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe") -Verb RunAs -ArgumentList ($args -join " ")
	exit
}

Close-SolidWorksForUninstall

$registered = Get-RegisteredAddinPath
$candidates = @($registered, $PackageAddin) | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
foreach ($addin in $candidates) {
	if ((Test-Path -LiteralPath $addin) -and (Test-Path -LiteralPath $RegAsm)) {
		try { & $RegAsm $addin /u | Write-Host } catch { }
	}
}

foreach ($root in @("HKLM:\SOFTWARE\SolidWorks", "HKCU:\Software\SolidWorks")) {
	Remove-Item -Path (Join-Path $root "Addins\$Guid") -Recurse -Force -ErrorAction SilentlyContinue
	$startupRoot = Join-Path $root "AddInsStartup"
	Remove-ItemProperty -Path $startupRoot -Name $Guid -Force -ErrorAction SilentlyContinue
	Remove-Item -Path (Join-Path $startupRoot $Guid) -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $InstallRoot) {
	$legacyFull = [IO.Path]::GetFullPath($InstallRoot)
	$expected = [IO.Path]::GetFullPath("$env:ProgramData\XY\XYRadialMenu.SW")
	if ($legacyFull.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
		Get-ChildItem -LiteralPath $legacyFull -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
	}
}

Write-Host "Uninstall completed."
Write-Host "Package files were kept: $PackageRoot"
Write-Host "User configuration was kept in AppData."
