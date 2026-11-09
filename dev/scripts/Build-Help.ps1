[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/help' }
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.VersionPrefix
$topics = Get-Content -LiteralPath (Join-Path $repo 'docs/help-topics.json') -Raw | ConvertFrom-Json -AsHashtable
$pages = @('docs/handleiding.md', 'docs/installatie.md', 'docs/problemen-oplossen.md',
    'docs/klic-bronnen.md', 'docs/template-maatvoering.md', 'docs/diagnostische-commandos.md', 'README.md', 'SECURITY.md')
$pageIds = @{}
foreach ($page in $pages) { $pageIds[[IO.Path]::GetFullPath((Join-Path $repo $page))] = [IO.Path]::GetFileNameWithoutExtension($page).ToLowerInvariant() }
$sections = [Collections.Generic.List[string]]::new()
$navigation = [Collections.Generic.List[string]]::new()
$samples = @{}
foreach ($page in $pages) {
    $path = [IO.Path]::GetFullPath((Join-Path $repo $page))
    $id = $pageIds[$path]
    $html = (ConvertFrom-Markdown -LiteralPath $path).Html
    $html = [regex]::Replace($html, '<img\b(?=[^>]*src="https?://)[^>]*alt="([^"]*)"[^>]*>', '$1')
    $title = [regex]::Match($html, '<h1[^>]*>(.*?)</h1>').Groups[1].Value
    $html = [regex]::Replace($html, 'id="([^"]+)"', { param($m) 'id="' + $id + '--' + $m.Groups[1].Value + '"' })
    $html = [regex]::Replace($html, '(href|src)="([^"]+)"', {
        param($m)
        $attribute = $m.Groups[1].Value
        $link = [Net.WebUtility]::HtmlDecode($m.Groups[2].Value)
        if ($link -match '^(https?://|mailto:)') {
            if ($attribute -eq 'src') { throw "Afbeelding heeft internet nodig: $link" }
            return $m.Value
        }
        if ($link.StartsWith('#')) { return 'href="#' + $id + '--' + $link.Substring(1) + '"' }
        $parts = $link.Split('#', 2)
        $target = [IO.Path]::GetFullPath((Join-Path (Split-Path $path) ([Uri]::UnescapeDataString($parts[0]))))
        if (!$target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $target -PathType Leaf)) {
            throw "Ongeldige verwijzing in ${page}: $link"
        }
        if ($attribute -eq 'src') {
            $mime = switch ([IO.Path]::GetExtension($target)) { '.png' { 'image/png' } '.svg' { 'image/svg+xml' } default { throw "Onbekende afbeelding: $link" } }
            return 'src="data:' + $mime + ';base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($target)) + '" loading="lazy"'
        }
        if ($pageIds.ContainsKey($target)) {
            $anchor = $pageIds[$target]
            if ($parts.Length -gt 1) { $anchor += '--' + $parts[1] }
            return 'href="#' + $anchor + '"'
        }
        if ([IO.Path]::GetExtension($target) -eq '.json') {
            $anchor = 'voorbeeld-' + [IO.Path]::GetFileNameWithoutExtension($target)
            $samples[$anchor] = $target
            return 'href="#' + $anchor + '"'
        }
        $relative = [IO.Path]::GetRelativePath($repo, $target).Replace('\', '/')
        return 'href="https://github.com/joligej/infracad-helper/blob/v' + $version + '/' + $relative + $(if ($parts.Length -gt 1) { '#' + $parts[1] }) + '"'
    })
    $sections.Add('<article id="' + $id + '">' + $html + '</article>')
    $navigation.Add('<a href="#' + $id + '">' + $title + '</a>')
    if ($id -eq 'handleiding') {
        foreach ($heading in [regex]::Matches($html, '<h2 id="([^"]+)">(.*?)</h2>')) {
            $navigation.Add('<a href="#' + $heading.Groups[1].Value + '">' + $heading.Groups[2].Value + '</a>')
        }
    }
}
foreach ($sample in $samples.GetEnumerator() | Sort-Object Key) {
    $sections.Add('<article><h1 id="' + $sample.Key + '">' + [IO.Path]::GetFileName($sample.Value) + '</h1><pre><code>' +
        [Net.WebUtility]::HtmlEncode((Get-Content -LiteralPath $sample.Value -Raw)) + '</code></pre></article>')
}
$body = $sections -join "`n"
$ids = @([regex]::Matches($body, 'id="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
foreach ($group in $topics.Values) {
    foreach ($topic in $group.Values) { if ($topic -cnotin $ids) { throw "Helponderwerp ontbreekt: $topic" } }
}
foreach ($command in $topics.commands.Keys | Sort-Object) {
    $target = $topics.commands[$command]
    $body = [regex]::Replace($body, '(<h[1-6] id="' + [regex]::Escape($target) + '")', '<span id="' + $command + '"></span>$1')
}
$template = Get-Content -LiteralPath (Join-Path $repo 'dev/help/page.html') -Raw
$result = $template.Replace('{{VERSION}}', $version).Replace('{{NAV}}', ($navigation -join "`n")).Replace('{{CONTENT}}', $body)
$ids = @([regex]::Matches($result, 'id="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if (($ids | Sort-Object -Unique).Count -ne $ids.Count) { throw 'Dubbele HTML-ankers.' }
foreach ($link in [regex]::Matches($result, 'href="#([^"]+)"')) {
    if ($link.Groups[1].Value -cnotin $ids) { throw "Ontbrekend anker: $($link.Value)" }
}
foreach ($command in $topics.commands.Keys) { if ($command -cnotin $ids) { throw "Commando mist een hulpanker: $command" } }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'NlcsLegenda.html'), $result)
# windows laat bij een lokale bestandskoppeling soms het fragment weg
$entries = @('handleiding') + @($topics.commands.Keys) + @($topics.Values | ForEach-Object { $_.Values })
foreach ($entry in $entries | Sort-Object -Unique) {
    if ($entry -cnotin $ids) { throw "Verwijspagina mist onderwerp: $entry" }
    $redirect = '<!doctype html><html lang="nl"><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=NlcsLegenda.html#' + $entry + '"><title>NLCS Legenda – hulp</title></head><body><a href="NlcsLegenda.html#' + $entry + '">Open de uitleg</a></body></html>'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ('help-' + $entry + '.html')), $redirect)
}
Write-Host "Offline hulp: $($pages.Count) pagina's, $($topics.commands.Count) commando's, $($topics.dialogs.Count) vensters."
