$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
[pscustomobject]@{
    Administrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
} | ConvertTo-Json
