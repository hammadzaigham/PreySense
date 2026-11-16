$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'PreySense.Core\Resources\Rgb\OemUsbKeyboardLayouts.json'
$bytes = [System.IO.File]::ReadAllBytes($source)
$output = [System.IO.File]::Create($source + '.gz')
try {
    $gzip = [System.IO.Compression.GZipStream]::new($output, [System.IO.Compression.CompressionLevel]::SmallestSize, $true)
    try { $gzip.Write($bytes, 0, $bytes.Length) }
    finally { $gzip.Dispose() }
} finally { $output.Dispose() }
Get-Item -LiteralPath ($source + '.gz') | Select-Object Name, Length
