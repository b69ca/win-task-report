$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$issues = New-Object System.Collections.Generic.List[object]
$rows = New-Object System.Collections.Generic.List[object]
$filter = [string]$env:UTIL_TASK_FILTER
$limit = [int]$env:UTIL_TASK_LIMIT
$includeMicrosoft = $env:UTIL_TASK_MICROSOFT -eq '1'
function TimeOrNull($value) {
    if ($null -eq $value -or $value.Year -lt 1971) { return $null }
    return $value.ToUniversalTime().ToString('o')
}
try {
    Import-Module ScheduledTasks -ErrorAction Stop
    $enumerationErrors = @()
    $all = @(Get-ScheduledTask -ErrorAction SilentlyContinue -ErrorVariable enumerationErrors | Where-Object {
        ($includeMicrosoft -or -not $_.TaskPath.StartsWith('\Microsoft\', [StringComparison]::OrdinalIgnoreCase)) -and
        (($_.TaskPath + $_.TaskName).IndexOf($filter, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    } | Sort-Object TaskPath, TaskName)
    foreach ($err in $enumerationErrors | Select-Object -First 20) {
        $issues.Add(@{area='enumeration';message=$err.Exception.Message})
    }
    if ($all.Count -gt $limit) { $issues.Add(@{area='limit';message="Only the first $limit of $($all.Count) matched tasks were collected."}) }
    foreach ($task in ($all | Select-Object -First $limit)) {
        $row = [ordered]@{name=$task.TaskName;path=$task.TaskPath;state=$task.State.ToString();actionCount=@($task.Actions).Count;infoAvailable=$false;lastResult=$null;missedRuns=$null;lastRunUtc=$null;nextRunUtc=$null}
        try {
            $info = $task | Get-ScheduledTaskInfo -ErrorAction Stop
            $row.lastResult = ([long]$info.LastTaskResult -band 4294967295L)
            $row.missedRuns = [long]$info.NumberOfMissedRuns
            $row.lastRunUtc = TimeOrNull $info.LastRunTime
            $row.nextRunUtc = TimeOrNull $info.NextRunTime
            $row.infoAvailable = $true
        } catch {
            $row.lastResult = $null; $row.missedRuns = $null; $row.lastRunUtc = $null; $row.nextRunUtc = $null
            $issues.Add(@{area=($task.TaskPath+$task.TaskName);message=$_.Exception.Message})
        }
        $rows.Add($row)
    }
    @{evidence=@{filter=$filter;limit=$limit;includeMicrosoft=$includeMicrosoft;matchedCount=$all.Count;tasks=@($rows.ToArray())};issues=@($issues.ToArray())} | ConvertTo-Json -Depth 10 -Compress
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
