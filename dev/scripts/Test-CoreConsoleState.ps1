#requires -Version 7.4
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CoreConsole.ps1')
$testKey = 'HKCU:\Software\NlcsLegendaTests\' + [Guid]::NewGuid().ToString('N')
$count = 0
try {
    New-Item -Path $testKey -Force | Out-Null
    foreach ($original in @(0, 1)) {
        New-ItemProperty -LiteralPath $testKey -Name FileDialog -Value $original -PropertyType DWord -Force | Out-Null
        $before = Get-NlcsRegistryValue $testKey FileDialog
        New-ItemProperty -LiteralPath $testKey -Name FileDialog -Value (1 - $original) -PropertyType DWord -Force | Out-Null
        $restored = @(Restore-NlcsProfileState @($before))
        if ($restored.Count -ne 1 -or !(Compare-NlcsRegistryValue $before (Get-NlcsRegistryValue $testKey FileDialog))) {
            throw "FILEDIA=$original niet exact hersteld."
        }
        if (@(Restore-NlcsProfileState @($before)).Count -ne 0) { throw 'Ongewijzigde waarde onterecht hersteld.' }
        $count += 2
    }
    New-ItemProperty -LiteralPath $testKey -Name SECURELOAD -Value '2' -PropertyType String -Force | Out-Null
    $before = Get-NlcsRegistryValue $testKey SECURELOAD
    New-ItemProperty -LiteralPath $testKey -Name SECURELOAD -Value 0 -PropertyType DWord -Force | Out-Null
    Restore-NlcsProfileState @($before) | Out-Null
    if (!(Compare-NlcsRegistryValue $before (Get-NlcsRegistryValue $testKey SECURELOAD))) { throw 'Registertype verloren.' }
    $count++
    $before = Get-NlcsRegistryValue $testKey MissingValue
    New-ItemProperty -LiteralPath $testKey -Name MissingValue -Value 1 -PropertyType DWord | Out-Null
    Restore-NlcsProfileState @($before) | Out-Null
    if ((Get-NlcsRegistryValue $testKey MissingValue).Exists) { throw 'Oorspronkelijk ontbrekende waarde is blijven staan.' }
    $count++
    $emptyBefore = @()
    New-ItemProperty -LiteralPath $testKey -Name NewProfileValue -Value 0 -PropertyType DWord | Out-Null
    $changes = @(Get-NlcsProfileChanges $emptyBefore @((Get-NlcsRegistryValue $testKey NewProfileValue)))
    if ($changes.Count -ne 1 -or $changes[0].Exists) { throw 'Nieuw profiel niet herkend.' }
    Restore-NlcsProfileState $changes | Out-Null
    if ((Get-NlcsRegistryValue $testKey NewProfileValue).Exists) { throw 'Nieuwe profielwaarde niet verwijderd.' }
    $count++
    foreach ($scriptFile in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
        $tokens = $null; $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count -gt 0) { throw ($errors -join "`n") }
    }
    "STATE PASS: $count herstelcontroles; PowerShell-syntax gecontroleerd."
}
finally {
    # Alleen de zojuist gemaakte lege testsleutel; geen gebruikersprofiel of recursieve verwijdering.
    if (Test-Path -LiteralPath $testKey) { Remove-Item -LiteralPath $testKey -Force }
}
