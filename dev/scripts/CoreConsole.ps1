#requires -Version 7.4

function Get-NlcsProfileState {
    # filedia uit filedialog; sysvarmonitor alleen als voorkeur
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

function Get-NlcsLogProfileState {
    $root = 'HKCU:\Software\Autodesk\AutoCAD'
    if (!(Test-Path -LiteralPath $root)) { return }
    foreach ($series in Get-ChildItem -LiteralPath $root) {
        foreach ($product in Get-ChildItem -LiteralPath $series.PSPath) {
            $profiles = Join-Path $product.PSPath 'Profiles'
            if (!(Test-Path -LiteralPath $profiles)) { continue }
            foreach ($profile in Get-ChildItem -LiteralPath $profiles) {
                Get-NlcsRegistryValue (Join-Path $profile.PSPath 'Editor Configuration') 'LogFilePath'
                Get-NlcsRegistryValue (Join-Path $profile.PSPath 'General') 'LogFileOpen'
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
        [hashtable]$Environment = @{},
        [switch]$Isolated
    )
    if ($Isolated) {
        return Invoke-NlcsIsolatedConsole -Executable $Executable -Drawing $Drawing -Script $Script -Log $Log -TimeoutSeconds $TimeoutSeconds -Environment $Environment
    }
    $mutex = [Threading.Mutex]::new($false, 'Local\NlcsLegendaCoreConsole')
    $locked = $false
    $process = $null
    $before = @()
    $logBefore = @()
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
        $logBefore = @(Get-NlcsLogProfileState)
        $before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$Log.profile-before.json" -Encoding utf8
        $logBefore | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$Log.logging-before.json" -Encoding utf8
        $work = Split-Path -Parent ([IO.Path]::GetFullPath($Script))
        $arguments = @('/i', ('"{0}"' -f $Drawing), '/s', ('"{0}"' -f $Script), '/l', 'en-US')
        # geen doelkeuze erven uit de bovenliggende sessie
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
            # bij afbreken uitsluitend het eigen proces stoppen
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
                $logAfter = @(Get-NlcsLogProfileState)
                $logAfter | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$Log.logging-after.json" -Encoding utf8
                $logChanged = @(Get-NlcsProfileChanges $logBefore $logAfter)
                if ($changed.Count + $logChanged.Count -gt 0) {
                    if (Get-Process acad, accoreconsole -ErrorAction SilentlyContinue) {
                        throw "CAD is tijdens de test gestart; profielherstel is niet veilig. Beginwaarden: $Log.profile-before.json"
                    }
                    $restored = @(Restore-NlcsProfileState $changed)
                    # opgeslagen logpad en logstand herstellen vóór opruimen
                    Restore-NlcsProfileState $logChanged | Out-Null
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

# eigen cad-profiel voor gelijktijdige tests; geen bewijs van normale autoload
function Invoke-NlcsIsolatedConsole {
    param([string]$Executable, [string]$Drawing, [string]$Script, [string]$Log,
          [int]$TimeoutSeconds, [hashtable]$Environment)
    $work = Split-Path -Parent ([IO.Path]::GetFullPath($Script))
    $id = 'nlcs-' + [Guid]::NewGuid().ToString('N')
    $profile = Join-Path $work $id
    New-Item -ItemType Directory -Path $profile | Out-Null
    $wrapped = "$Log.isolated.scr"
    '(princ (strcat "\nNLCS_ISOLATION=" (getvar "ROAMABLEROOTPREFIX")))' + "`n" + (Get-Content -LiteralPath $Script -Raw) |
        Set-Content -LiteralPath $wrapped -Encoding ascii
    $arguments = @('/i', ('"{0}"' -f $Drawing), '/s', ('"{0}"' -f $wrapped), '/l', 'en-US', '/isolate', $id, ('"{0}"' -f $profile))
    $childEnvironment = @{ NLCSLEGENDA_TARGET = $null }
    foreach ($name in $Environment.Keys) { $childEnvironment[$name] = $Environment[$name] }
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory $work `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput $Log -RedirectStandardError "$Log.stderr" -Environment $childEnvironment
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) { throw "Core Console overschrijdt $TimeoutSeconds seconden. Log: $Log" }
        if ($process.ExitCode -ne 0) { throw "Core Console eindigt met procescode $($process.ExitCode). Log: $Log" }
        $plain = (Get-Content -LiteralPath $Log -Raw).Replace([string][char]0, '')
        if (!$plain.Contains('NLCS_ISOLATION=' + $profile)) { throw "Afzonderlijk CAD-profiel niet bevestigd. Log: $Log" }
    }
    finally {
        if ($process) {
            try { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() } }
            finally { $process.Dispose() }
        }
    }
    # profiel van een andere cad-sessie ongemoeid laten
    [pscustomobject]@{ ExitCode=0; Log=$Log; ProfileValuesChecked=0; Isolated=$true; IsolationRoot=$profile }
}
