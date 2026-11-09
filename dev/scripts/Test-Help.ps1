[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $PSScriptRoot 'Build-Help.ps1')
$topics = Get-Content -LiteralPath (Join-Path $repo 'docs/help-topics.json') -Raw | ConvertFrom-Json -AsHashtable
$code = (Get-ChildItem (Join-Path $repo 'src/NlcsLegenda.Plugin') -Filter '*.cs' | Get-Content -Raw) -join "`n"
$attributes = [regex]::Matches($code, '\[CommandMethod\("NLCS_LEGENDA", "(NLCS\w+)", null, CommandFlags\.\w+, null, "help-\1.html", ""\)\]')
if ($attributes.Count -ne $topics.commands.Count) { throw 'Niet ieder gebruikerscommando heeft F1-hulp.' }
$manual = Get-Content -LiteralPath (Join-Path $repo 'docs/handleiding.md') -Raw
foreach ($command in [regex]::Matches($manual, '\| `(NLCS\w+)` \|')) {
    if (!$topics.commands.ContainsKey($command.Groups[1].Value)) { throw "Commando zonder uitleg: $command" }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repo 'deploy/NlcsLegenda.bundle/PackageContents.xml') -Raw
$commands = $manifest.ApplicationPackage.Components.ComponentEntry.Commands.Command
if ($commands.Count -ne $topics.commands.Count) { throw 'Bundel mist gebruikerscommandos.' }
foreach ($command in $commands) {
    if (!$topics.commands.ContainsKey($command.Global) -or $command.HelpTopic -cne ('#' + $command.Global)) { throw "Verkeerde bundelhulp: $($command.Global)" }
}
$path = Join-Path $repo 'artifacts/help/NlcsLegenda.html'
$first = (Get-FileHash -LiteralPath $path).Hash
foreach ($entry in @('handleiding') + @($topics.commands.Keys) + @($topics.Values | ForEach-Object { $_.Values }) | Sort-Object -Unique) {
    $redirect = Get-Content -LiteralPath (Join-Path (Split-Path $path) ('help-' + $entry + '.html')) -Raw
    if (!$redirect.Contains('url=NlcsLegenda.html#' + $entry + '"')) { throw "Verkeerde doorverwijzing: $entry" }
}
& (Join-Path $PSScriptRoot 'Build-Help.ps1')
if ((Get-FileHash -LiteralPath $path).Hash -ne $first) { throw 'Dezelfde bronnen leveren verschillende hulpbestanden op.' }
$html = Get-Content -LiteralPath $path -Raw
if ($html -match '(src|srcset)="https?://' -or $html -match '<link\b' -or $html -match '\{\{(VERSION|CONTENT|NAV)\}\}') { throw 'Offline bestand is onvolledig.' }
Write-Host "HELP PASS: commandos, onderwerpen, interne links, afbeeldingen en reproduceerbare uitvoer."
