#requires -Version 7.4

function Get-NlcsProfileState {
    # FILEDIA wordt als FileDialog bewaard. SysvarMonitor bevat alleen de gewenste waarde.
    $root = 'HKCU:\Software\Autodesk\AutoCAD'
    if (!(Test-Path -LiteralPath $root)) { return }
    foreach ($series in Get-ChildItem -LiteralPath $root) {
        foreach ($product in Get-ChildItem -LiteralPath $series.PSPath) {
            $fixed = Join-Path $product.PSPath 'FixedProfile\General Configuration'
            if (Test-Path -LiteralPath $fixed) {
                foreach ($name in @('FileDialog', 'CommandDialogs')) {
                    Get-NlcsRegistryValue $fixed $name
                }
            }
            $profiles = Join-Path $product.PSPath 'Profiles'
            if (!(Test-Path -LiteralPath $profiles)) { continue }
            foreach ($profile in Get-ChildItem -LiteralPath $profiles) {
                $variables = Join-Path $profile.PSPath 'Variables'
                if (Test-Path -LiteralPath $variables) {
                    Get-NlcsRegistryValue $variables 'SECURELOAD'
                }
            }
        }
    }
}

function Get-NlcsRegistryValue([string]$Path, [string]$Name) {
    if (!(Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Path=$Path; Name=$Name; Exists=$false; Value=$null; Kind=$null }
    }
    $key = Get-Item -LiteralPath $Path -ErrorAction Stop
    try {
        $exists = $key.GetValueNames() -contains $Name
        [pscustomobject]@{
            Path = $Path; Name = $Name; Exists = $exists
            Value = if ($exists) { $key.GetValue($Name, $null, 'DoNotExpandEnvironmentNames') } else { $null }
            Kind = if ($exists) { $key.GetValueKind($Name).ToString() } else { $null }
        }
    }
    finally { $key.Dispose() }
}

function Compare-NlcsRegistryValue($Before, $After) {
    $Before.Exists -eq $After.Exists -and $Before.Kind -eq $After.Kind -and
        (ConvertTo-Json -InputObject $Before.Value -Compress) -ceq (ConvertTo-Json -InputObject $After.Value -Compress)
}

function Get-NlcsProfileChanges([object[]]$Before, [object[]]$After) {
    $old = @{}; $current = @{}
    foreach ($value in $Before) { $old[$value.Path + '|' + $value.Name] = $value }
    foreach ($value in $After) { $current[$value.Path + '|' + $value.Name] = $value }
    foreach ($key in @($old.Keys) + @($current.Keys) | Select-Object -Unique) {
        $beforeValue = $old[$key]; $afterValue = $current[$key]
        if (!$beforeValue) {
            $beforeValue = [pscustomobject]@{ Path=$afterValue.Path; Name=$afterValue.Name; Exists=$false; Value=$null; Kind=$null }
        }
        if (!$afterValue) { $afterValue = Get-NlcsRegistryValue $beforeValue.Path $beforeValue.Name }
        if (!(Compare-NlcsRegistryValue $beforeValue $afterValue)) { $beforeValue }
    }
}

function Restore-NlcsProfileState([object[]]$Before) {
    foreach ($value in $Before) {
        $after = Get-NlcsRegistryValue $value.Path $value.Name
        if (Compare-NlcsRegistryValue $value $after) { continue }
        $subkey = $value.Path -replace '^.*HKEY_CURRENT_USER\\', '' -replace '^HKCU:\\', ''
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subkey, $true)
        if ($null -eq $key) { throw "Profielsleutel niet schrijfbaar: $($value.Path)" }
        try {
            if ($value.Exists) { $key.SetValue($value.Name, $value.Value, $value.Kind) }
            else { $key.DeleteValue($value.Name, $false) }
        }
        finally { $key.Dispose() }
        if (!(Compare-NlcsRegistryValue $value (Get-NlcsRegistryValue $value.Path $value.Name))) {
            throw "Profielwaarde niet hersteld: $($value.Path) / $($value.Name)"
        }
        "$($value.Path) / $($value.Name)"
    }
}

function Invoke-NlcsCoreConsole {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$Drawing,
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$Log,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds = 300,
        [hashtable]$Environment = @{}
    )
    $mutex = [Threading.Mutex]::new($false, 'Local\NlcsLegendaCoreConsole')
    $locked = $false
    $process = $null
    $before = @()
    $failure = $null
    $restored = @()
    try {
        try { $locked = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $locked = $true }
        if (!$locked) { throw 'Er draait al een NLCS-test. Voer CAD-tests na elkaar uit.' }
        if (Get-Process acad, accoreconsole -ErrorAction SilentlyContinue) {
            throw 'Sluit AutoCAD/Civil 3D en wacht tot andere Core Console-taken klaar zijn.'
        }
        $before = @(Get-NlcsProfileState)
        $before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$Log.profile-before.json" -Encoding utf8
        $work = Split-Path -Parent ([IO.Path]::GetFullPath($Script))
        $arguments = @('/i', ('"{0}"' -f $Drawing), '/s', ('"{0}"' -f $Script), '/l', 'en-US')
        # Elke run kiest zijn doel zelf; een instelling uit de bovenliggende shell mag niet meeliften.
        $childEnvironment = @{ NLCSLEGENDA_TARGET = $null }
        foreach ($name in $Environment.Keys) { $childEnvironment[$name] = $Environment[$name] }
        $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory $work `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput $Log -RedirectStandardError "$Log.stderr" `
            -Environment $childEnvironment
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Core Console overschrijdt $TimeoutSeconds seconden. Log: $Log"
        }
        if ($process.ExitCode -ne 0) { throw "Core Console eindigt met procescode $($process.ExitCode). Log: $Log" }
    }
    catch { $failure = $_ }
    finally {
        try {
            # Ook bij Ctrl+C/fouten eerst uitsluitend ons eigen proces stoppen en laten afsluiten.
            if ($null -ne $process) {
                try {
                    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
                }
                finally { $process.Dispose() }
            }
            if ($null -ne $process) {
                $after = @(Get-NlcsProfileState)
                $after | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$Log.profile-after.json" -Encoding utf8
                $changed = @(Get-NlcsProfileChanges $before $after)
                if ($changed.Count -gt 0) {
                    if (Get-Process acad, accoreconsole -ErrorAction SilentlyContinue) {
                        throw "CAD is tijdens de test gestart; profielherstel is niet veilig. Beginwaarden: $Log.profile-before.json"
                    }
                    $restored = @(Restore-NlcsProfileState $changed)
                }
            }
        }
        finally {
            if ($locked) { $mutex.ReleaseMutex() }
            $mutex.Dispose()
        }
    }
    if ($failure) { throw $failure }
    if ($restored.Count -gt 0) { throw "De test lekte profielwaarden; exact hersteld: $($restored -join ', ')" }
    [pscustomobject]@{ ExitCode = 0; Log = $Log; ProfileValuesChecked = @(@($before) + @($after) | ForEach-Object { $_.Path + '|' + $_.Name } | Select-Object -Unique).Count }
}
