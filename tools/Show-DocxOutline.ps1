param([Parameter(Mandatory = $true)][string]$Path, [int]$Limit = 60)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'word/document.xml' }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $xml = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally { $zip.Dispose() }

$count = 0
foreach ($node in $xml.document.body.ChildNodes) {
    if ($node.LocalName -eq 'tbl') { Write-Output '[table]'; continue }
    if ($node.LocalName -ne 'p') { continue }

    $style = 'Normal'
    if ($node.pPr -and $node.pPr.pStyle) { $style = $node.pPr.pStyle.val }

    $text = ($node.GetElementsByTagName('w:t') | ForEach-Object { $_.InnerText }) -join ''
    if (-not $text.Trim()) { continue }

    $clipped = $text.Substring(0, [Math]::Min(95, $text.Length))
    Write-Output ("{0,-16} | {1}" -f $style, $clipped)

    $count++
    if ($count -ge $Limit) { break }
}
