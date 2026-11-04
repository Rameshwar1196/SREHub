<#
.SYNOPSIS
    Imports exported Aras Knowledgebase .docx articles into a SRE Hub learning subject.

.DESCRIPTION
    For each .docx the script extracts the text, strips the browser print header/footer
    artifacts, infers headings, rebuilds tables, and writes a Markdown article plus a
    copy of the original document under a slugified, SRE-Hub-safe name.

    File and folder names are slugified because SRE Hub rejects path segments that do not
    start with a letter or digit, or that contain characters outside [A-Za-z0-9 ._-].

.EXAMPLE
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
    .\tools\Import-KbArticles.ps1 -SourcePath '..\KB Documents (DAMON) 2\KB Documents (DAMON)' `
                                  -DestinationPath '.\learning-content\14-work-journal\kb'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DestinationPath,

    # Copy the original .docx next to each generated Markdown article.
    [bool]$IncludeOriginals = $true,

    # Process only the first N documents; 0 means all.
    [int]$Limit = 0,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$CategoryMap = [ordered]@{
    'Practices and Work Instructions' = 'practices-and-work-instructions'
    'Guidelines And Enablement'       = 'guidelines-and-enablement'
    'Subscriber Resources'            = 'subscriber-resources'
    'Enablement'                      = 'solution-delivery-enablement'
    'Field Enablement Resources'      = 'field-enablement-resources'
    'SaaS 3.0 Enablement'             = 'saas-3-enablement'
    'Technical Knowledge Sharing'     = 'technical-knowledge-sharing'
    'Knowledge base'                  = 'finops-knowledge-base'
    'Product Security'                = 'product-security'
    'ARK'                             = 'ark'
    'Migration to SaaS Projects'      = 'migration-to-saas-projects'
    'Feedback'                        = 'feedback'
}

$KnownSections = @(
    'Description', 'Overview', 'Purpose', 'Scope', 'Summary', 'Background', 'Context',
    'Prerequisites', 'Pre-requisites', 'Requirements', 'Assumptions',
    'Steps', 'Procedure', 'Process', 'Instructions', 'Work instructions', 'Implementation',
    'Configuration', 'Setup', 'Installation', 'Usage', 'Examples', 'Example',
    'Test methodology', 'Testing', 'Validation', 'Verification', 'Acceptance criteria',
    'Troubleshooting', 'Known issues', 'Limitations', 'Rollback', 'Recovery',
    'References', 'Related articles', 'Related', 'See also', 'Links', 'Attachments',
    'Notes', 'Note', 'Roles and responsibilities', 'Responsibilities', 'Definitions'
)

function Get-Slug {
    param([string]$Value, [int]$MaxLength = 80)

    $text = $Value.ToLowerInvariant()
    $text = $text -replace '[\u2018\u2019\u201c\u201d]', ''
    $text = $text -replace '[^a-z0-9]+', '-'
    $text = $text.Trim('-')

    if ($text.Length -gt $MaxLength) {
        $text = $text.Substring(0, $MaxLength).Trim('-')
    }

    if (-not $text -or $text -notmatch '^[a-z0-9]') {
        $text = "article-$text".Trim('-')
    }

    return $text
}

function Get-ArticleParts {
    param([string]$BaseName)

    # "(7) Speed and latency testing - Practices and Work Instructions - Global Cloud Services - Aras Knowledgebase"
    $title = $BaseName -replace '^\([^)]{0,4}\)\s*', ''
    $category = 'uncategorised'

    foreach ($section in $CategoryMap.Keys) {
        if ($title -match [regex]::Escape(" - $section - ")) {
            $category = $CategoryMap[$section]
            $title = ($title -split [regex]::Escape(" - $section - "))[0]
            break
        }
    }

    if ($category -eq 'uncategorised') {
        $title = $title -replace '\s*-\s*Aras Knowledgebase$', ''
    }

    $title = $title.Trim().TrimEnd('.')
    $title = $title -replace '_\s*', ': '

    return [PSCustomObject]@{
        Title    = $title
        Category = $category
    }
}

function Read-DocxBlock {
    param([string]$Path)

    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'word/document.xml' }
        if (-not $entry) { return @() }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $xml = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $zip.Dispose()
    }

    $blocks = @()

    foreach ($node in $xml.document.body.ChildNodes) {
        switch ($node.LocalName) {
            'p' {
                $text = (($node.GetElementsByTagName('w:t') | ForEach-Object { $_.InnerText }) -join '').Trim()
                if (-not $text) { break }

                $isList = $null -ne $node.pPr -and $null -ne $node.pPr.numPr
                $blocks += [PSCustomObject]@{ Kind = 'p'; Text = $text; IsList = $isList }
            }
            'tbl' {
                $rows = @()
                foreach ($tr in $node.GetElementsByTagName('w:tr')) {
                    $cells = @()
                    foreach ($tc in $tr.GetElementsByTagName('w:tc')) {
                        $cells += (($tc.GetElementsByTagName('w:t') | ForEach-Object { $_.InnerText }) -join '').Trim()
                    }

                    # Print export wraps the page footer in a one-row table.
                    $joined = ($cells -join ' ')
                    if ($joined -match 'kb\.aras\.com/\S+\s*\d+/\d+' -or $joined -match '^\d{1,2}/\d{1,2}/\d{2,4},\s*\d{1,2}:\d{2}\s*(AM|PM)') { continue }

                    if ($cells.Count -gt 0 -and $joined.Trim()) { $rows += , $cells }
                }
                if ($rows.Count -gt 0) {
                    $blocks += [PSCustomObject]@{ Kind = 'table'; Rows = $rows }
                }
            }
        }
    }

    return $blocks
}

function Split-NumberedRun {
    param([string]$Text)

    # Print export merges consecutive list items: "8. Press the button. 9. Record results."
    return ($Text -split '(?<=[.\)\u201d])\s*(?=\d{1,2}\.\s)') | Where-Object { $_.Trim() }
}

function Test-SectionHeading {
    param([string]$Text, [string]$NextText)

    if ($Text.Length -gt 70) { return $false }
    if ($Text -match '[.,;:]$') { return $false }
    if ($Text -match '^\d+[\.\)]\s') { return $false }
    if ($Text -match '^https?://') { return $false }

    if ($KnownSections -contains $Text.TrimEnd(':')) { return $true }

    return $Text -cmatch '^[A-Z]' -and $NextText -and $NextText.Length -gt 70
}

function Join-WrappedParagraph {
    param([object[]]$Body)

    # The print export emits one paragraph per rendered line, so sentences arrive split.
    $merged = @()

    foreach ($block in $Body) {
        $previous = if ($merged.Count -gt 0) { $merged[-1] } else { $null }

        $canMerge = $null -ne $previous -and
        $previous.Kind -eq 'p' -and $block.Kind -eq 'p' -and
        -not $previous.IsList -and -not $block.IsList -and
        $previous.Text -notmatch '[.:;!?\u201d]$' -and
        $previous.Text.Length -gt 40 -and
        $block.Text -cmatch '^[a-z(\u201c]' -and
        ($KnownSections -notcontains $block.Text.TrimEnd(':'))

        if ($canMerge) {
            $previous.Text = ($previous.Text.TrimEnd() + ' ' + $block.Text.TrimStart())
            continue
        }

        $merged += [PSCustomObject]@{ Kind = $block.Kind; Text = $block.Text; IsList = $block.IsList; Rows = $block.Rows }
    }

    return $merged
}

function ConvertTo-MarkdownArticle {
    param([object[]]$Blocks, [string]$Title, [string]$Category, [string]$OriginalName, [string]$SourceDocx)

    $sourceUrl = $null
    $articleId = $null
    $exported = $null
    $tags = $null
    $body = @()
    $lines = @()

    foreach ($block in $Blocks) {
        if ($block.Kind -eq 'table') { $body += $block; continue }

        $text = $block.Text

        # Footer: "https://kb.aras.com/gcs/a/gcs-work-instructions/SD792/speed-and-latency-testing 1/3"
        if ($text -match '^(?<url>https?://kb\.aras\.com/\S+?)\s+\d+/\d+$') {
            if (-not $sourceUrl) {
                $sourceUrl = $Matches['url']
                if ($sourceUrl -match '/([A-Z]{2,}\d+)/') { $articleId = $Matches[1] }
            }
            continue
        }

        # Header: "7/29/26, 12:39 PM (7) Speed and latency testing - ..."
        if ($text -match '^(?<stamp>\d{1,2}/\d{1,2}/\d{2,4},\s*\d{1,2}:\d{2}\s*(AM|PM))') {
            if (-not $exported) { $exported = $Matches['stamp'] }
            continue
        }

        if ($text -eq $Title) { continue }

        # The export prints the article's tag list as one paragraph, separated by icon-font glyphs.
        if ($body.Count -eq 0 -and -not $tags -and $text.Length -lt 300 -and
            ($text -match '[\uE000-\uF8FF]' -or ([regex]::Matches($text, '[a-z][A-Z]')).Count -ge 2)) {
            $tags = (($text -split '[\uE000-\uF8FF]' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) -join ', ')
            if ($tags) { continue }
        }

        $text = ($text -replace '[\uE000-\uF8FF]', ' ') -replace '\s{2,}', ' '
        $text = $text.Trim()
        if (-not $text) { continue }

        foreach ($part in (Split-NumberedRun -Text $text)) {
            $body += [PSCustomObject]@{ Kind = 'p'; Text = $part.Trim(); IsList = $block.IsList; Rows = $null }
        }
    }

    $body = Join-WrappedParagraph -Body $body

    $lines += "# $Title"
    $lines += ''
    $lines += '> Aras internal knowledgebase article. Confidential - do not redistribute.'
    $lines += ''
    $lines += "- **Category:** $Category"
    if ($articleId) { $lines += "- **Article ID:** $articleId" }
    if ($sourceUrl) { $lines += "- **Source:** <$sourceUrl>" }
    if ($exported) { $lines += "- **Exported:** $exported" }
    if ($tags) { $lines += "- **Tags:** $tags" }
    $lines += "- **Original:** [$OriginalName](source/$SourceDocx)"
    $lines += ''
    $lines += '---'
    $lines += ''

    for ($i = 0; $i -lt $body.Count; $i++) {
        $block = $body[$i]

        if ($block.Kind -eq 'table') {
            $rows = $block.Rows
            $width = ($rows | ForEach-Object { $_.Count } | Measure-Object -Maximum).Maximum
            if ($width -lt 1) { continue }

            $header = @()
            for ($c = 0; $c -lt $width; $c++) {
                $value = if ($c -lt $rows[0].Count) { $rows[0][$c] } else { '' }
                $header += ($value -replace '\|', '\|')
            }

            $lines += '| ' + ($header -join ' | ') + ' |'
            $lines += '|' + (' --- |' * $width)

            for ($r = 1; $r -lt $rows.Count; $r++) {
                $cells = @()
                for ($c = 0; $c -lt $width; $c++) {
                    $value = if ($c -lt $rows[$r].Count) { $rows[$r][$c] } else { '' }
                    $cells += ($value -replace '\|', '\|')
                }
                $lines += '| ' + ($cells -join ' | ') + ' |'
            }

            $lines += ''
            continue
        }

        $next = ''
        if ($i + 1 -lt $body.Count -and $body[$i + 1].Kind -eq 'p') { $next = $body[$i + 1].Text }

        if (-not $block.IsList -and (Test-SectionHeading -Text $block.Text -NextText $next)) {
            $lines += ''
            $lines += '## ' + $block.Text.TrimEnd(':')
            $lines += ''
            continue
        }

        if ($block.IsList) {
            $lines += "- $($block.Text)"
            continue
        }

        if ($block.Text -match '^\d{1,2}[\.\)]\s') {
            $lines += $block.Text
            $lines += ''
            continue
        }

        $lines += $block.Text
        $lines += ''
    }

    # Collapse runs of blank lines.
    $output = @()
    foreach ($line in $lines) {
        if ($line -eq '' -and $output.Count -gt 0 -and $output[-1] -eq '') { continue }
        $output += $line
    }

    return ($output -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine
}

# ----------------------------------------------------------------------------- run

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Source folder not found: $SourcePath"
}

# .NET file APIs use the process working directory, which is not the PowerShell location.
$SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$DestinationPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $DestinationPath))

$documents = @(Get-ChildItem -LiteralPath $SourcePath -Filter '*.docx' -File | Sort-Object Name)
if ($Limit -gt 0) { $documents = $documents | Select-Object -First $Limit }

if ($documents.Count -eq 0) {
    throw "No .docx files found in $SourcePath"
}

Write-Host ""
Write-Host ("  Importing {0} article(s)" -f $documents.Count) -ForegroundColor Cyan
Write-Host ("  Source      : {0}" -f (Resolve-Path -LiteralPath $SourcePath).Path) -ForegroundColor DarkGray
Write-Host ("  Destination : {0}" -f $DestinationPath) -ForegroundColor DarkGray
Write-Host ""

$results = @()
$index = 0

# The export numbers repeat downloads "(1) x.docx", "(7) x.docx"; keep only the newest of each article.
$planned = @()
$duplicates = 0

foreach ($document in $documents) {
    $parts = Get-ArticleParts -BaseName $document.BaseName
    $planned += [PSCustomObject]@{
        File     = $document
        Title    = $parts.Title
        Category = $parts.Category
        Slug     = Get-Slug -Value $parts.Title
    }
}

$deduplicated = @()

foreach ($group in ($planned | Group-Object { "$($_.Category)|$($_.Slug)" })) {
    $winner = $group.Group | Sort-Object { $_.File.LastWriteTime }, { $_.File.Length } -Descending | Select-Object -First 1
    $deduplicated += $winner

    if ($group.Count -gt 1) {
        $duplicates += $group.Count - 1
        Write-Verbose ("Duplicate export(s) of '{0}' ignored: {1}" -f $winner.Title,
            (($group.Group | Where-Object { $_.File.FullName -ne $winner.File.FullName } | ForEach-Object { $_.File.Name }) -join '; '))
    }
}

$deduplicated = $deduplicated | Sort-Object Category, Slug

if ($duplicates -gt 0) {
    Write-Host ("  {0} duplicate export(s) ignored; {1} unique article(s) remain." -f $duplicates, $deduplicated.Count) -ForegroundColor DarkYellow
    Write-Host ""
}

foreach ($item in $deduplicated) {
    $index++
    $document = $item.File
    $parts = [PSCustomObject]@{ Title = $item.Title; Category = $item.Category }
    $slug = $item.Slug
    $categoryDirectory = Join-Path $DestinationPath $parts.Category
    $markdownPath = Join-Path $categoryDirectory "$slug.md"
    $originalPath = Join-Path (Join-Path $categoryDirectory 'source') "$slug.docx"

    if ((Test-Path -LiteralPath $markdownPath) -and -not $Force) {
        Write-Host ("  [{0,3}/{1}] skip  {2}" -f $index, $deduplicated.Count, $slug) -ForegroundColor DarkYellow
        $results += [PSCustomObject]@{ Title = $parts.Title; Category = $parts.Category; Slug = $slug; Status = 'Skipped' }
        continue
    }

    try {
        $blocks = Read-DocxBlock -Path $document.FullName

        if ($blocks.Count -eq 0) {
            throw 'No readable content.'
        }

        $markdown = ConvertTo-MarkdownArticle -Blocks $blocks -Title $parts.Title -Category $parts.Category `
            -OriginalName $document.Name -SourceDocx "$slug.docx"

        if ($PSCmdlet.ShouldProcess($markdownPath, 'Write article')) {
            New-Item -ItemType Directory -Path $categoryDirectory -Force | Out-Null
            [System.IO.File]::WriteAllText($markdownPath, $markdown, (New-Object System.Text.UTF8Encoding($false)))

            if ($IncludeOriginals) {
                New-Item -ItemType Directory -Path (Split-Path $originalPath -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $document.FullName -Destination $originalPath -Force
            }
        }

        Write-Host ("  [{0,3}/{1}] ok    {2}/{3}" -f $index, $deduplicated.Count, $parts.Category, $slug) -ForegroundColor Green
        $results += [PSCustomObject]@{ Title = $parts.Title; Category = $parts.Category; Slug = $slug; Status = 'Imported' }
    }
    catch {
        Write-Host ("  [{0,3}/{1}] FAIL  {2} - {3}" -f $index, $deduplicated.Count, $slug, $_.Exception.Message) -ForegroundColor Red
        $results += [PSCustomObject]@{ Title = $parts.Title; Category = $parts.Category; Slug = $slug; Status = 'Failed' }
    }
}

# ----------------------------------------------------------------------------- index

if ($PSCmdlet.ShouldProcess((Join-Path $DestinationPath 'README.md'), 'Write index')) {
    $imported = @($results | Where-Object { $_.Status -ne 'Failed' })

    $index = @()
    $index += '# Aras Knowledgebase (reference copy)'
    $index += ''
    $index += '> **Aras confidential.** This folder is a reference copy of internal knowledgebase articles.'
    $index += '> It is *not* part of the portable work journal - exclude `kb/` from anything you take with you,'
    $index += '> and never commit it to a personal repository.'
    $index += ''
    $index += ("Generated by ``tools/Import-KbArticles.ps1`` on {0}. {1} article(s)." -f (Get-Date -Format 'yyyy-MM-dd'), $imported.Count)
    $index += ''
    $index += 'Each article has a converted Markdown version (searchable in SRE Hub) and the original'
    $index += '`.docx` alongside it under `<category>/source/`. The Markdown is text-only: images are not'
    $index += 'carried over, so open the original when you need screenshots or exact formatting.'
    $index += ''
    $index += '| Category | Articles |'
    $index += '| --- | --- |'

    foreach ($group in ($imported | Group-Object Category | Sort-Object Count -Descending)) {
        $index += ("| [{0}]({0}/README.md) | {1} |" -f $group.Name, $group.Count)
    }

    $index += ''

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $DestinationPath 'README.md'),
        (($index -join [Environment]::NewLine) + [Environment]::NewLine),
        (New-Object System.Text.UTF8Encoding($false)))

    foreach ($group in ($imported | Group-Object Category)) {
        $categoryLines = @()
        $categoryLines += "# $($group.Name)"
        $categoryLines += ''
        $categoryLines += ("{0} article(s). [Back to the knowledgebase index](../README.md)." -f $group.Count)
        $categoryLines += ''

        foreach ($article in ($group.Group | Sort-Object Title)) {
            $categoryLines += ("- [{0}]({1}.md)" -f $article.Title, $article.Slug)
        }

        $categoryLines += ''

        [System.IO.File]::WriteAllText(
            (Join-Path (Join-Path $DestinationPath $group.Name) 'README.md'),
            (($categoryLines -join [Environment]::NewLine) + [Environment]::NewLine),
            (New-Object System.Text.UTF8Encoding($false)))
    }
}

Write-Host ""
Write-Host ("  Imported {0}, skipped {1}, failed {2}" -f
    @($results | Where-Object { $_.Status -eq 'Imported' }).Count,
    @($results | Where-Object { $_.Status -eq 'Skipped' }).Count,
    @($results | Where-Object { $_.Status -eq 'Failed' }).Count) -ForegroundColor Cyan
Write-Host ""
