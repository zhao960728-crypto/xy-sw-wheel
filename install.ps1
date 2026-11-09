param(

	[switch]$ValidateOnly,

	[switch]$OverwriteConfig,

	[string]$SolidWorksPath

)



$ErrorActionPreference = "Stop"

$Version = "1.0.1327.326"

$Guid = "{8D97B869-0F3D-4F13-901A-0A63D9D13A12}"

$ProductName = "XY轮盘"

$ProductDescription = "XY SOLIDWORKS 插件"

$PackageRoot = [IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))

$PluginFolderName = -join ([char[]](0x63D2, 0x4EF6, 0x6587, 0x4EF6))

$PackagePluginDir = Join-Path $PackageRoot $PluginFolderName

$PackageAddinDll = Join-Path $PackagePluginDir "XY.SolidWorksPlugin.SwAddin.dll"

# Some hosts (unattended/sandboxed shells) do not expose %ProgramData%. Falling

# back to the system drive keeps every path absolute instead of silently

# installing to the root of the current drive.

if ([string]::IsNullOrWhiteSpace($env:ProgramData)) {

	$env:ProgramData = Join-Path $env:SystemDrive "ProgramData"

}

$InstallRoot = "$env:ProgramData\XY\XYRadialMenu.SW"

$TargetDir = Join-Path $InstallRoot $Version

$AddinDll = Join-Path $TargetDir "XY.SolidWorksPlugin.SwAddin.dll"

$RegAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

$script:ResolvedSolidWorksHostDirectory = $null



Add-Type -AssemblyName System.Windows.Forms



function Find-JsonContaining([string]$marker, [string]$description) {

	foreach ($item in Get-ChildItem -LiteralPath $PackageRoot -Recurse -File -Filter "*.json" -ErrorAction SilentlyContinue) {

		$text = [IO.File]::ReadAllText($item.FullName, [Text.Encoding]::UTF8)

		if ($text.Contains($marker)) { return $item }

	}

	throw "Missing package file: $description"

}



function Test-Administrator {

	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()

	$principal = New-Object Security.Principal.WindowsPrincipal($identity)

	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

}



function Restart-Elevated {

	$args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")

	if ($OverwriteConfig) { $args += "-OverwriteConfig" }

	if (![string]::IsNullOrWhiteSpace($SolidWorksPath)) { $args += @("-SolidWorksPath", "`"$SolidWorksPath`"") }

	$process = Start-Process -FilePath (Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe") -Verb RunAs -ArgumentList ($args -join " ") -Wait -PassThru

	return $process.ExitCode

}



function Get-DotNetRelease {

	$key = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"

	if (!(Test-Path $key)) { return 0 }

	return [int](Get-ItemProperty -LiteralPath $key -Name Release -ErrorAction SilentlyContinue).Release

}



function Resolve-SolidWorksExecutableValue([string]$value) {

	if ([string]::IsNullOrWhiteSpace($value)) { return $null }

	$expanded = [Environment]::ExpandEnvironmentVariables($value).Trim()

	$candidate = $expanded

	$quoted = [regex]::Match($expanded, '^\s*"([^"]+)"')

	if ($quoted.Success) {

		$candidate = $quoted.Groups[1].Value

	} elseif ($expanded -match '(?i)^(.+?SLDWORKS\.exe)(?:\s|,|$)') {

		$candidate = $Matches[1]

	}

	$candidate = [regex]::Replace($candidate.Trim().Trim('"'), ',\s*\d+\s*$', '')

	if (Test-Path -LiteralPath $candidate -PathType Container) {

		$candidate = Join-Path $candidate "SLDWORKS.exe"

	}

	if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { return $null }

	if (![IO.Path]::GetFileName($candidate).Equals("SLDWORKS.exe", [StringComparison]::OrdinalIgnoreCase)) { return $null }

	return [IO.Path]::GetFullPath($candidate)

}



function Add-SolidWorksExecutableCandidate($items, $seen, [string]$value, [string]$source) {

	$executable = Resolve-SolidWorksExecutableValue $value

	if ($null -eq $executable -or !$seen.Add($executable)) { return }

	$items.Add([PSCustomObject]@{ Executable = $executable; Source = $source }) | Out-Null

}



function Get-SolidWorksExecutableCandidates([bool]$IncludeComRegistration = $true) {

	$items = New-Object System.Collections.Generic.List[object]

	$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

	Add-SolidWorksExecutableCandidate $items $seen $SolidWorksPath "installer parameter"



	foreach ($process in @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)) {

		try { Add-SolidWorksExecutableCandidate $items $seen $process.Path "running process" } catch { }

	}



	if ($IncludeComRegistration) {

		$progIdKeys = @(

			"Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID",

			"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\SldWorks.Application\CLSID",

			"Registry::HKEY_CURRENT_USER\Software\Classes\SldWorks.Application\CLSID"

		)

		foreach ($applicationKey in $progIdKeys) {

			if (!(Test-Path -LiteralPath $applicationKey)) { continue }

			$solidWorksClsid = [string](Get-Item -LiteralPath $applicationKey).GetValue("")

			if ([string]::IsNullOrWhiteSpace($solidWorksClsid)) { continue }

			foreach ($serverKey in @(

				"Registry::HKEY_CLASSES_ROOT\CLSID\$solidWorksClsid\LocalServer32",

				"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$solidWorksClsid\LocalServer32",

				"Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$solidWorksClsid\LocalServer32"

			)) {

				if (Test-Path -LiteralPath $serverKey) {

					Add-SolidWorksExecutableCandidate $items $seen ([string](Get-Item -LiteralPath $serverKey).GetValue("")) "COM registration"

				}

			}

		}

	}



	foreach ($appPathKey in @(

		"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe",

		"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe",

		"Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe"

	)) {

		if (!(Test-Path -LiteralPath $appPathKey)) { continue }

		$appPathItem = Get-Item -LiteralPath $appPathKey

		Add-SolidWorksExecutableCandidate $items $seen ([string]$appPathItem.GetValue("")) "Windows App Paths"

		Add-SolidWorksExecutableCandidate $items $seen ([string]$appPathItem.GetValue("Path")) "Windows App Paths folder"

	}



	foreach ($solidWorksRoot in @("HKLM:\SOFTWARE\SolidWorks", "HKLM:\SOFTWARE\WOW6432Node\SolidWorks", "HKCU:\SOFTWARE\SolidWorks")) {

		if (!(Test-Path -LiteralPath $solidWorksRoot)) { continue }

		$versionKeys = @(Get-ChildItem -LiteralPath $solidWorksRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '(?i)^SOLIDWORKS\s+\d{4}$' } | Sort-Object { if ($_.PSChildName -match '(\d{4})') { [int]$Matches[1] } else { 0 } } -Descending)

		foreach ($versionKey in $versionKeys) {

			foreach ($registryKey in @($versionKey.PSPath, (Join-Path $versionKey.PSPath "Setup"))) {

				if (!(Test-Path -LiteralPath $registryKey)) { continue }

				$properties = Get-ItemProperty -LiteralPath $registryKey -ErrorAction SilentlyContinue

				foreach ($name in @("SolidWorks Folder", "InstallLocation", "Install Location", "InstallDir", "Install Dir", "Path")) {

					$property = $properties.PSObject.Properties[$name]

					if ($null -ne $property) { Add-SolidWorksExecutableCandidate $items $seen ([string]$property.Value) "SOLIDWORKS setup registry" }

				}

			}

		}

	}



	foreach ($uninstallRoot in @(

		"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",

		"Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"

	)) {

		if (!(Test-Path -LiteralPath $uninstallRoot)) { continue }

		foreach ($key in Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue) {

			$properties = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue

			if ([string]$properties.DisplayName -notmatch '(?i)^SOLIDWORKS(?:\s|$)') { continue }

			Add-SolidWorksExecutableCandidate $items $seen ([string]$properties.InstallLocation) "installed programs registry"

			Add-SolidWorksExecutableCandidate $items $seen ([string]$properties.DisplayIcon) "installed programs registry"

		}

	}



	foreach ($programRoot in @($env:ProgramW6432, $env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {

		$corporationRoot = Join-Path $programRoot "SOLIDWORKS Corp"

		if (!(Test-Path -LiteralPath $corporationRoot)) { continue }

		foreach ($directory in Get-ChildItem -LiteralPath $corporationRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "SOLIDWORKS*" }) {

			Add-SolidWorksExecutableCandidate $items $seen $directory.FullName "standard installation folder"

		}

	}

	return $items.ToArray()

}



function Test-SolidWorksHostDirectory([string]$directory) {

	if ([string]::IsNullOrWhiteSpace($directory)) { return $false }

	foreach ($name in @("SLDWORKS.exe", "SolidWorks.Interop.sldworks.dll", "SolidWorks.Interop.swconst.dll", "SolidWorks.Interop.swcommands.dll", "SolidWorks.Interop.swpublished.dll")) {

		if (!(Test-Path -LiteralPath (Join-Path $directory $name) -PathType Leaf)) { return $false }

	}

	return $true

}



function Select-SolidWorksExecutable([string]$initialDirectory) {

	$dialog = New-Object System.Windows.Forms.OpenFileDialog

	$dialog.Title = "Select the SOLIDWORKS main program (SLDWORKS.exe)"

	$dialog.Filter = "SOLIDWORKS main program (SLDWORKS.exe)|SLDWORKS.exe"

	$dialog.FileName = "SLDWORKS.exe"

	$dialog.CheckFileExists = $true

	if (![string]::IsNullOrWhiteSpace($initialDirectory) -and (Test-Path -LiteralPath $initialDirectory)) { $dialog.InitialDirectory = $initialDirectory }

	try {

		if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.FileName }

		return $null

	} finally {

		$dialog.Dispose()

	}

}



function Convert-ToVersionOrNull([string]$value) {

	try { return [version]$value } catch { return $null }

}



function Get-AddinAssemblyVersion([string]$path) {

	if (!(Test-Path -LiteralPath $path)) { throw "Missing add-in DLL for version check: $path" }

	return [Reflection.AssemblyName]::GetAssemblyName($path).Version

}



function Get-SolidWorksHostDirectory([bool]$IncludeComRegistration = $true, [bool]$AllowPrompt = $true) {

	if (Test-SolidWorksHostDirectory $script:ResolvedSolidWorksHostDirectory) { return $script:ResolvedSolidWorksHostDirectory }

	$candidates = @(Get-SolidWorksExecutableCandidates $IncludeComRegistration)

	foreach ($candidate in $candidates) {

		$directory = Split-Path -Parent $candidate.Executable

		if (Test-SolidWorksHostDirectory $directory) {

			$script:ResolvedSolidWorksHostDirectory = $directory

			$script:SolidWorksPath = $candidate.Executable

			Write-Host "Detected SOLIDWORKS: $($candidate.Executable) [$($candidate.Source)]"

			return $directory

		}

	}



	if ($AllowPrompt -and [Environment]::UserInteractive) {

		$initialDirectory = if ($candidates.Count -gt 0) { Split-Path -Parent $candidates[0].Executable } else { $null }

		$selected = Select-SolidWorksExecutable $initialDirectory

		if (![string]::IsNullOrWhiteSpace($selected)) {

			$directory = Split-Path -Parent $selected

			if (!(Test-SolidWorksHostDirectory $directory)) {

				throw "The selected folder is missing required SOLIDWORKS API components. Select SLDWORKS.exe from a complete SOLIDWORKS installation, not a shortcut or reduced copy."

			}

			$script:ResolvedSolidWorksHostDirectory = [IO.Path]::GetFullPath($directory)

			$script:SolidWorksPath = [IO.Path]::GetFullPath($selected)

			return $script:ResolvedSolidWorksHostDirectory

		}

	}



	if ($candidates.Count -gt 0) {

		throw "SLDWORKS.exe was found, but its folder is missing required SOLIDWORKS API components. Repair SOLIDWORKS with SOLIDWORKS Installation Manager, then try again."

	}

	throw 'SLDWORKS.exe was not found. Start SOLIDWORKS once and retry. If it still fails, repair SOLIDWORKS or run install.ps1 -SolidWorksPath C:\path\to\SLDWORKS.exe.'

}



function Get-RegistrationInteropNames {

	return @(

		"SolidWorks.Interop.sldworks.dll",

		"SolidWorks.Interop.swconst.dll",

		"SolidWorks.Interop.swcommands.dll",

		"SolidWorks.Interop.swpublished.dll"

	)

}



function Assert-PackageInteropCompatibility([string]$addinDll) {

	# ReflectionOnlyLoadFrom is unreliable on some Framework hosts ("not supported

	# on this platform"), and LoadFrom keeps the DLL mapped. Loading from a byte

	# array gives us the reference set without locking the file or depending on

	# the reflection-only path.

	$bytes = [IO.File]::ReadAllBytes($addinDll)

	$references = [Reflection.Assembly]::Load($bytes).GetReferencedAssemblies()

	foreach ($name in Get-RegistrationInteropNames) {

		$source = Join-Path $PackagePluginDir $name

		if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing packaged SOLIDWORKS interop assembly: $source" }

		$sourceAssembly = [Reflection.AssemblyName]::GetAssemblyName($source)

		$reference = $references | Where-Object { $_.Name.Equals($sourceAssembly.Name, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1

		if ($null -ne $reference -and !$sourceAssembly.FullName.Equals($reference.FullName, [StringComparison]::OrdinalIgnoreCase)) {

			throw "Packaged SOLIDWORKS interop version mismatch. Required=$($reference.FullName) Actual=$($sourceAssembly.FullName) Path=$source"

		}

	}

}



function Invoke-RegAsmWithInterop([string]$addinDll, [switch]$Unregister) {

	$interopStates = New-Object System.Collections.Generic.List[object]

	$registrationExitCode = -1

	try {

		foreach ($name in Get-RegistrationInteropNames) {

			$source = Join-Path $PackagePluginDir $name

			$target = Join-Path (Split-Path -Parent $addinDll) $name

			if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing packaged SOLIDWORKS interop assembly required for registration: $source" }

			$backup = $null

			if (Test-Path -LiteralPath $target -PathType Leaf) {

				$backup = [IO.Path]::GetTempFileName()

				Copy-Item -LiteralPath $target -Destination $backup -Force

			}

			$interopStates.Add([PSCustomObject]@{ Target = $target; Backup = $backup })

			Copy-Item -LiteralPath $source -Destination $target -Force

		}



		$arguments = @($addinDll)

		if ($Unregister) { $arguments += "/u" }

		else { $arguments += "/codebase" }

		# Some non-interactive hosts do not propagate $LASTEXITCODE from a direct

		# "&" call, and elevated children sometimes run under ConstrainedLanguage

		# which blocks the managed registration type. Launching RegAsm through

		# Start-Process works in both cases and keeps a real exit code.

		try

		{

			$proc = Start-Process -FilePath $RegAsm -ArgumentList $arguments -Wait -PassThru -NoNewWindow

			if ($proc.ExitCode -eq 0) { $registrationExitCode = 0 }

			else { Write-Host ("RegAsm reported exit code {0}" -f $proc.ExitCode) }

		}

		catch

		{

			# Nothing is skipped: the managed registration below still runs. Say

			# so, because "skipped" reads as a machine left without an add-in.

			Write-Host "RegAsm could not be started; falling back to managed COM registration."

		}

		if ($registrationExitCode -ne 0)

		{

			# Unregistering by loading an assembly would cache that old identity

			# and make the next registration reuse it, so the caller handles

			# removal instead.

			if ($Unregister) { throw "RegAsm failed to unregister. Exit code: $registrationExitCode" }

			Invoke-ManagedComRegistration $addinDll

			$registrationExitCode = 0

		}

	} finally {

		# Restoring an interop copy can fail while a host still holds it. A

		# leftover copy is harmless, so this must never fail the install.

		foreach ($state in $interopStates) {

			try {

				if ($null -ne $state.Backup) {

					Copy-Item -LiteralPath $state.Backup -Destination $state.Target -Force -ErrorAction Stop

					Remove-Item -LiteralPath $state.Backup -Force -ErrorAction SilentlyContinue

				} else {

					Remove-Item -LiteralPath $state.Target -Force -ErrorAction SilentlyContinue

				}

			} catch {

				Write-Host ("Interop copy left in place: " + (Split-Path -Leaf $state.Target))

			}

		}

	}

	if ($registrationExitCode -ne 0) { throw "RegAsm failed. Exit code: $registrationExitCode" }

}



# Registers the add-in through the framework's own registration services.

# Produces the same COM registration as "RegAsm /codebase" without launching an

# external tool, so it works where RegAsm cannot be started.

function Invoke-ManagedComRegistration([string]$addinDll, [switch]$Unregister) {

	$assembly = [System.Reflection.Assembly]::Load([IO.File]::ReadAllBytes($addinDll))

	$services = New-Object System.Runtime.InteropServices.RegistrationServices

	if ($Unregister)

	{

		if (!$services.UnregisterAssembly($assembly))

		{

			Write-Host "Managed unregistration reported nothing to remove: $addinDll"

			return

		}

		Write-Host "Unregistered add-in (managed): $addinDll"

		return

	}

	$null = $services.RegisterAssembly($assembly, [System.Runtime.InteropServices.AssemblyRegistrationFlags]::SetCodeBase)

	Write-Host "Registered add-in (managed): $addinDll"

}



function Register-Addin([string]$addinDll) {

	Invoke-RegAsmWithInterop $addinDll

}



function Get-InstalledVersionDirectories {

	if (!(Test-Path -LiteralPath $InstallRoot)) { return @() }

	return @(Get-ChildItem -LiteralPath $InstallRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {

		$versionValue = Convert-ToVersionOrNull $_.Name

		if ($null -ne $versionValue) {

			[PSCustomObject]@{

				Version = $versionValue

				Path = $_.FullName

				Name = $_.Name

			}

		}

	})

}



function Get-RegisteredAddinPath {

	$key = "Registry::HKEY_CLASSES_ROOT\CLSID\$Guid\InprocServer32"

	if (!(Test-Path -LiteralPath $key)) { return $null }

	$codeBase = [string](Get-ItemProperty -LiteralPath $key -Name CodeBase -ErrorAction SilentlyContinue).CodeBase

	if ([string]::IsNullOrWhiteSpace($codeBase)) { return $null }

	try { return [IO.Path]::GetFullPath(([Uri]$codeBase).LocalPath) }

	catch { return [IO.Path]::GetFullPath($codeBase) }

}



function Assert-PackageVersion {

	if (!(Test-Path -LiteralPath $PackageAddinDll)) { throw "Missing package add-in DLL: $PackageAddinDll" }

	$addin = Get-Item -LiteralPath $PackageAddinDll

	$packageVersion = Convert-ToVersionOrNull $Version

	if ($null -eq $packageVersion) { throw "Installer version is invalid: $Version" }

	$dllVersion = Get-AddinAssemblyVersion $addin.FullName

	if ($dllVersion -ne $packageVersion) {

		throw "Package DLL version mismatch. Installer=$Version DLL=$dllVersion"

	}

	$newer = @(Get-InstalledVersionDirectories | Where-Object { $_.Version -gt $packageVersion } | Sort-Object Version -Descending | Select-Object -First 1)

	if ($newer.Count -gt 0) {

		throw "A newer version is already installed: $($newer[0].Version). Refusing to downgrade to $Version."

	}

	$registered = Get-RegisteredAddinPath

	if ($null -ne $registered -and (Test-Path -LiteralPath $registered)) {

		$registeredVersion = Get-AddinAssemblyVersion $registered

		if ($registeredVersion -gt $packageVersion) {

			throw "A newer registered version is already installed: $registeredVersion. Refusing to downgrade to $Version."

		}

	}

}



function Assert-InstalledVersion([string]$addinDll) {

	$expected = Convert-ToVersionOrNull $Version

	$actual = Get-AddinAssemblyVersion $addinDll

	if ($actual -ne $expected) {

		throw "Installed DLL version mismatch. Expected=$Version Actual=$actual Path=$addinDll"

	}

}



function Remove-OldVersionDirectories {

	if (!(Test-Path -LiteralPath $InstallRoot)) { return }

	$rootFull = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

	$targetFull = [IO.Path]::GetFullPath($TargetDir)

	foreach ($dir in Get-InstalledVersionDirectories) {

		$dirFull = [IO.Path]::GetFullPath($dir.Path)

		if (!$dirFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {

			throw "Unsafe old version path: $dirFull"

		}

		if ($dirFull.Equals($targetFull, [StringComparison]::OrdinalIgnoreCase)) { continue }

		# The assembly just loaded for COM registration may still hold a handle,

		# so a leftover old directory must not fail an otherwise good install.

		try

		{

			Remove-Item -LiteralPath $dirFull -Recurse -Force -ErrorAction Stop

			Write-Host "Removed old version directory: $dirFull"

		}

		catch

		{

			Write-Host "Could not remove old version directory now: $dirFull"

			Write-Host "  It is harmless and will be cleaned up by a later install."

		}

	}

}



function Install-ProgramFiles {

	$targetFull = [IO.Path]::GetFullPath($TargetDir)

	$installRootFull = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

	if (!$targetFull.StartsWith($installRootFull, [StringComparison]::OrdinalIgnoreCase)) {

		throw "Unsafe install target: $targetFull"

	}

	New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

	foreach ($item in Get-ChildItem -LiteralPath $PackagePluginDir -Force) {

		if ($item.Name -like "error.log*") { continue }

		if ($item.Name -like "*.pdb") { continue }

		Copy-Item -LiteralPath $item.FullName -Destination $TargetDir -Recurse -Force

	}

	Assert-InstalledVersion $AddinDll

}



function Unregister-PreviousAddin([string]$newAddinDll) {

	$previous = Get-RegisteredAddinPath

	if ([string]::IsNullOrWhiteSpace($previous) -or !(Test-Path -LiteralPath $previous)) { return }

	if ([IO.Path]::GetFullPath($previous).Equals([IO.Path]::GetFullPath($newAddinDll), [StringComparison]::OrdinalIgnoreCase)) { return }

	try {

		Invoke-RegAsmWithInterop $previous -Unregister

		Write-Host "Unregistered previous add-in: $previous"

	} catch {

		# Remove the class registration directly rather than loading the old

		# assembly: a loaded assembly keeps its identity and would make the next

		# registration point at the old path.

		try {

			Unregister-ComClassByKey

			Write-Host "Unregistered previous add-in via class key: $previous"

		} catch {

			Write-Warning "Unable to unregister previous add-in: $previous. $($_.Exception.Message)"

		}

	}

}



function Unregister-ComClassByKey {

	$key = "Registry::HKEY_CLASSES_ROOT\CLSID\$Guid"

	if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction Stop }

	Remove-OldComVersionKeys

}



function Remove-OldComVersionKeys {

	$key = "Registry::HKEY_CLASSES_ROOT\CLSID\$Guid\InprocServer32"

	if (!(Test-Path -LiteralPath $key)) { return }

	foreach ($subKey in Get-ChildItem -LiteralPath $key -ErrorAction SilentlyContinue) {

		if ($subKey.PSChildName -eq $Version) { continue }

		Remove-Item -LiteralPath $subKey.PSPath -Recurse -Force -ErrorAction Stop

		Write-Host "Removed old COM version key: $($subKey.PSChildName)"

	}

}



function Write-Check([string]$name, [bool]$ok, [string]$detail = "") {

	if ($ok) { Write-Host ("  [ok]   " + $name + $(if ($detail) { " - " + $detail })) }

	else { Write-Host ("  [FAIL] " + $name + $(if ($detail) { " - " + $detail })) }

}



# Verifies every packaged file the add-in needs at run time. A missing interop

# or resource is reported by name instead of failing later as a load error.

function Test-PackageFiles {

	$missing = @()

	$required = @($PackageAddinDll)

	foreach ($name in Get-RegistrationInteropNames) {

		$item = Join-Path $PackagePluginDir $name

		if (Test-Path -LiteralPath $item -PathType Leaf) { $required += $item }

		else { $missing += $name }

	}

	foreach ($item in $required) {

		if (!(Test-Path -LiteralPath $item -PathType Leaf)) { $missing += (Split-Path -Leaf $item) }

	}

	$resources = Join-Path $PackagePluginDir "Resources"

	if (Test-Path -LiteralPath $resources -PathType Container) {

		foreach ($item in Get-ChildItem -LiteralPath $resources -File) {

			if (!(Test-Path -LiteralPath $item.FullName -PathType Leaf)) { $missing += $item.Name }

		}

	}

	return @($missing)

}



# True when the DLL can be opened for exclusive write, i.e. no running host has

# it loaded. A locked target would silently fail the copy later.

function Test-FileNotLocked([string]$path) {

	if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $true }

	try {

		$stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

		$stream.Close()

		$stream.Dispose()

		return $true

	} catch { return $false }

}



function Assert-Environment {

	Write-Host "Environment check"

	$problems = New-Object System.Collections.Generic.List[string]



	$hasDll = Test-Path -LiteralPath $PackageAddinDll

	Write-Check "Add-in assembly" $hasDll $PackageAddinDll

	if (!$hasDll) { $problems.Add("Missing add-in assembly: $PackageAddinDll") }



	$missing = Test-PackageFiles

	Write-Check "Package files" ($missing.Count -eq 0) $(if ($missing.Count) { "missing " + ($missing -join ", ") } else { "complete" })

	if ($missing.Count) { $problems.Add("Missing package files: " + ($missing -join ", ")) }



	$configOk = $true

	try { Find-JsonContaining '"TriggerMode"' "default config JSON" | Out-Null } catch { $configOk = $false }

	Write-Check "Default configuration JSON" $configOk

	if (!$configOk) { $problems.Add("Missing default configuration JSON") }



	$dotNet = Get-DotNetRelease

	$dotNetOk = $dotNet -ge 528040

	Write-Check ".NET Framework 4.8" $dotNetOk ("release " + $dotNet)

	if (!$dotNetOk) { $problems.Add("Requires .NET Framework 4.8 or newer") }



	$bitnessOk = [Environment]::Is64BitOperatingSystem

	Write-Check "64-bit Windows" $bitnessOk

	if (!$bitnessOk) { $problems.Add("Requires 64-bit Windows") }



	# Either registration route is enough: the managed fallback covers hosts

	# where RegAsm cannot be started.

	$regAsmOk = Test-Path -LiteralPath $RegAsm

	$managedOk = $true

	try { [System.Runtime.InteropServices.RegistrationServices] | Out-Null } catch { $managedOk = $false }

	Write-Check "COM registration" ($regAsmOk -or $managedOk) $(if ($regAsmOk) { "RegAsm" } else { "managed" })

	if (!($regAsmOk -or $managedOk)) { $problems.Add("Neither RegAsm nor managed COM registration is available") }



	$installRootExists = Test-Path -LiteralPath $InstallRoot -PathType Container

	$writable = Test-Path -LiteralPath ([IO.Path]::GetPathRoot($InstallRoot))

	Write-Check "Install location" $writable $InstallRoot

	if (!$writable) { $problems.Add("Install location unavailable: $InstallRoot") }

	if ($installRootExists) {

		$locked = Test-FileNotLocked $AddinDll

		Write-Check "Target DLL not in use" $locked $(if ($locked) { "replaceable" } else { "locked, close SOLIDWORKS first" })

		if (!$locked) { $problems.Add("Target DLL is in use: $AddinDll") }

	}



	try {

		$script:ResolvedSolidWorksHostDirectory = Get-SolidWorksHostDirectory -IncludeComRegistration $true -AllowPrompt (!$ValidateOnly)

		Write-Check "SOLIDWORKS host" ($null -ne $script:ResolvedSolidWorksHostDirectory) $script:ResolvedSolidWorksHostDirectory

	} catch {

		Write-Check "SOLIDWORKS host" $false $_.Exception.Message

		$problems.Add("SOLIDWORKS not found: " + $_.Exception.Message)

	}



	if ($problems.Count -gt 0) { throw ($problems -join "; ") }



	Assert-PackageInteropCompatibility $PackageAddinDll

	Write-Check "SOLIDWORKS interop assemblies" $true "compatible"

	Assert-PackageVersion

	Write-Check "Version consistency" $true $Version

}



function Get-UserConfigDir {

	$chineseFolder = -join ([char[]](0x58, 0x59, 0x8F6E, 0x76D8))

	return Join-Path ([Environment]::GetFolderPath("ApplicationData")) (Join-Path "XY" $chineseFolder)

}



function Set-AddinRegistry([string]$root) {

	$addin = Join-Path $root "Addins\$Guid"

	New-Item -Path $addin -Force | Out-Null

	New-ItemProperty -Path $addin -Name "(default)" -Value 0 -PropertyType DWord -Force | Out-Null

	New-ItemProperty -Path $addin -Name "Title" -Value $ProductName -PropertyType String -Force | Out-Null

	New-ItemProperty -Path $addin -Name "Description" -Value $ProductDescription -PropertyType String -Force | Out-Null

	$startupRoot = Join-Path $root "AddInsStartup"

	New-Item -Path $startupRoot -Force | Out-Null

	Remove-ItemProperty -Path $startupRoot -Name $Guid -Force -ErrorAction SilentlyContinue

	$startup = Join-Path $startupRoot $Guid

	New-Item -Path $startup -Force | Out-Null

	New-ItemProperty -Path $startup -Name "(default)" -Value 1 -PropertyType DWord -Force | Out-Null

}



function Get-SolidWorksApplication {

	try {

		return [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application")

	} catch {

		return $null

	}

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

	Write-Host "Environment check passed. Closing SOLIDWORKS automatically..."

	try {

		if ($null -ne $sw) { $sw.ExitApp() | Out-Null }

	} catch {

	}

	Start-Sleep -Milliseconds 500

	foreach ($process in @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)) {

		try {

			if ($process.MainWindowHandle -ne [IntPtr]::Zero) {

				$process.CloseMainWindow() | Out-Null

			}

		} catch {

		}

	}

}



function Close-SolidWorksForInstall {

	$processes = @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)

	if ($processes.Count -eq 0) { return }



	$sw = Get-SolidWorksApplication

	if ($null -ne $sw) {

		while ($true) {

			$unsaved = Get-UnsavedDocuments $sw

			if ($null -eq $unsaved) { break }

			if ($unsaved.Count -eq 0) { break }

			$message = "SOLIDWORKS has unsaved documents:`r`n`r`n" + ($unsaved -join "`r`n") + "`r`n`r`nPlease save them in SOLIDWORKS. After saving, click OK here; the installer will close SOLIDWORKS automatically and continue."

			[System.Windows.Forms.MessageBox]::Show($message, "XY Radial Menu Installer", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null

			Start-Sleep -Milliseconds 500

		}

	} else {

		$message = "SOLIDWORKS is running. The installer cannot connect to read unsaved document state, so SOLIDWORKS may show its own save prompt while closing. Save your work if prompted; the installer will continue after SOLIDWORKS exits."

		[System.Windows.Forms.MessageBox]::Show($message, "XY Radial Menu Installer", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null

	}



	Request-SolidWorksClose $sw



	$deadline = (Get-Date).AddSeconds(90)

	while ((Get-Date) -lt $deadline) {

		if (@(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue).Count -eq 0) { return }

		Start-Sleep -Milliseconds 500

	}

	throw "Unable to close SOLIDWORKS automatically. Close it manually, then run installer again."

}



function Install-DefaultConfig {

	$configSource = Find-JsonContaining '"TriggerMode"' "default config JSON"

	$configDir = Get-UserConfigDir

	$configTarget = Join-Path $configDir "solidworks-self-plugin.json"

	if ((Test-Path -LiteralPath $configTarget) -and !$OverwriteConfig) {

		Write-Host "Keeping existing user config: $configTarget"

		return

	}



	New-Item -ItemType Directory -Path $configDir -Force | Out-Null

	if (!$OverwriteConfig) {

		# Copy the existing wheel profile; source and its asset directories remain

		# available for rollback and absolute image paths remain valid.

		$legacyFolder = -join ([char[]](0x5C0F, 0x9633, 0x8F6E, 0x76D8))

		foreach ($legacyRoot in @("XY", "Mr.Z")) {

			$legacyConfig = Join-Path ([Environment]::GetFolderPath("ApplicationData")) (Join-Path $legacyRoot (Join-Path $legacyFolder "solidworks-self-plugin.json"))

			if (Test-Path -LiteralPath $legacyConfig -PathType Leaf) {

				Copy-Item -LiteralPath $legacyConfig -Destination $configTarget

				if ((Get-FileHash -LiteralPath $legacyConfig).Hash -ne (Get-FileHash -LiteralPath $configTarget).Hash) { throw "Profile migration hash mismatch." }

				Write-Host "Preserved existing wheel config: $legacyConfig -> $configTarget"

				return

			}

		}

	}

	$text = [IO.File]::ReadAllText($configSource.FullName, [Text.Encoding]::UTF8)



	$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

	[IO.File]::WriteAllText($configTarget, $text, $utf8NoBom)

	Write-Host "Installed default config: $configTarget"

}



try {

	Write-Host "$ProductName installer $Version"

	Assert-Environment

	if ($ValidateOnly) {

		Write-Host "Install environment check passed."

		exit 0

	}



	if (!(Test-Administrator)) {

		Write-Host "Administrator permission is required. Requesting elevation..."

		exit (Restart-Elevated)

	}



	Close-SolidWorksForInstall



	Get-ChildItem -LiteralPath $PackageRoot -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

	Install-ProgramFiles

	Get-ChildItem -LiteralPath $TargetDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

	Unregister-PreviousAddin $AddinDll

	Register-Addin $AddinDll

	Remove-OldComVersionKeys

	Set-AddinRegistry "HKLM:\SOFTWARE\SolidWorks"

	Set-AddinRegistry "HKCU:\Software\SolidWorks"

	Install-DefaultConfig

	Remove-OldVersionDirectories



	Write-Host "Install completed."

	Write-Host "Installed add-in DLL: $AddinDll"

	Write-Host "The extracted package can be moved or deleted after installation."

} catch {

	Write-Host ""

	Write-Host ("Install failed: " + $_.Exception.Message) -ForegroundColor Red

	Write-Host "Follow the instruction above, then run the installer again."

	exit 1

}





