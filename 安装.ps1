param(
	[switch]$ValidateOnly,
	[switch]$OverwriteConfig
)

& (Join-Path $PSScriptRoot "install.ps1") @PSBoundParameters
exit $LASTEXITCODE
