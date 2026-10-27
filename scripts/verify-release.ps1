param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$smokeRoot = Join-Path $projectRoot ('artifacts/ReleaseSmoke/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
$source = [IO.Path]::GetFullPath($ExePath)
$target = Join-Path $smokeRoot 'Nacre.exe'
Copy-Item -LiteralPath $source -Destination $target
[IO.File]::WriteAllText((Join-Path $smokeRoot 'portable.flag'), '')
$script:appProcess = $null
$script:rootElement = $null
$masterPassword = 'fake06'
$keyValue = 'fictional-release-token'

function Wait-For([scriptblock]$Predicate, [int]$TimeoutSeconds = 20) {
    $end = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $end) {
        if (& $Predicate) { return }
        if ($script:appProcess -and $script:appProcess.HasExited) { throw 'Release application exited unexpectedly.' }
        Start-Sleep -Milliseconds 100
    }
    throw 'Timed out waiting for the release UI.'
}
function Find-Id([string]$Id) {
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $all = $script:rootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($element in $all) { if (!$element.Current.IsOffscreen) { return $element } }
    return $null
}
function Set-Value([string]$Id, [string]$Value) {
    $element = Find-Id $Id
    if (!$element) { throw "UI element unavailable: $Id" }
    $pattern = $element.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}
function Invoke-Id([string]$Id) {
    $element = Find-Id $Id
    if (!$element) { throw "Button unavailable: $Id" }
    $pattern = $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
function Start-Nacre {
    $info = [Diagnostics.ProcessStartInfo]::new($target)
    $info.WorkingDirectory = $smokeRoot
    $info.UseShellExecute = $false
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $smokeRoot 'no-runtime-installed'
    $info.EnvironmentVariables['DOTNET_ROOT_X64'] = $info.EnvironmentVariables['DOTNET_ROOT']
    $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $script:appProcess = [Diagnostics.Process]::Start($info)
    Wait-For {
        $script:appProcess.Refresh()
        if ($script:appProcess.MainWindowHandle -eq [IntPtr]::Zero) { return $false }
        $script:rootElement = [Windows.Automation.AutomationElement]::FromHandle($script:appProcess.MainWindowHandle)
        return $null -ne (Find-Id 'MasterPassword')
    }
}
function Stop-Nacre {
    $null = $script:appProcess.CloseMainWindow()
    if (!$script:appProcess.WaitForExit(10000)) { throw 'Release application did not close cleanly.' }
    $script:appProcess.Dispose(); $script:appProcess = $null
}
try {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Nacre
    $startupMs = $watch.ElapsedMilliseconds
    Set-Value 'MasterPassword' $masterPassword
    Set-Value 'ConfirmPassword' $masterPassword
    (Find-Id 'Acknowledge').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Id 'Unlock'
    Wait-For { $null -ne (Find-Id 'New') -and (Find-Id 'New').Current.IsEnabled }
    $vault = Join-Path $smokeRoot 'Data/vault.nacre'
    if (!(Test-Path -LiteralPath $vault)) { throw 'Portable storage was not created beside the executable.' }
    $initial = [IO.File]::ReadAllText($vault)
    Invoke-Id 'New'
    Wait-For { $null -ne (Find-Id 'RecordTitle') }
    Set-Value 'RecordTitle' 'Fictional release record'
    Set-Value 'FieldName' 'TOKEN'
    Set-Value 'MaskedValue' $keyValue
    Invoke-Id 'Save'
    Wait-For { (Find-Id 'Save').Current.IsEnabled -and [IO.File]::ReadAllText($vault) -ne $initial }
    $saved = [IO.File]::ReadAllText($vault)
    if ($saved.Contains($keyValue) -or $saved.Contains($masterPassword) -or $saved.Contains('Fictional release record')) {
        throw 'Plaintext found in the release vault.'
    }
    $script:appProcess.Refresh()
    $workingSetMiB = [Math]::Round($script:appProcess.WorkingSet64 / 1MB, 1)
    Stop-Nacre
    Start-Nacre
    Set-Value 'MasterPassword' $masterPassword
    Invoke-Id 'Unlock'
    Wait-For { $null -ne (Find-Id 'RecordTitle') -and (Find-Id 'Save').Current.IsEnabled }
    $title = (Find-Id 'RecordTitle').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($title -ne 'Fictional release record') { throw 'Saved record was not recovered after restart.' }
    Invoke-Id 'CopyField'
    Wait-For { (Get-Clipboard -Raw) -eq $keyValue }
    Invoke-Id 'Lock'
    Wait-For { $null -ne (Find-Id 'MasterPassword') }
    if ((Get-Clipboard -Raw) -eq $keyValue) { throw 'Owned sensitive clipboard was not cleared on lock.' }
    Stop-Nacre
    $result = [ordered]@{ passed = $true; executable = $source; portableStore = $vault; startupMilliseconds = $startupMs; workingSetMiB = $workingSetMiB; checks = @('self-contained startup with invalid DOTNET_ROOT', 'portable storage', 'create master password', 'credential save', 'encrypted payload', 'clean exit', 'restart and unlock', 'credential copy', 'lock clipboard cleanup') }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $smokeRoot 'result.json') -Encoding UTF8
    Write-Output ($result | ConvertTo-Json -Depth 4)
} finally {
    if ($script:appProcess -and !$script:appProcess.HasExited) {
        $null = $script:appProcess.CloseMainWindow()
        if (!$script:appProcess.WaitForExit(3000)) { $script:appProcess.Kill(); $script:appProcess.WaitForExit() }
    }
    # The isolated directory contains only an encrypted, fictional test vault and is kept for inspection.
}
