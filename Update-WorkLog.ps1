<#
.SYNOPSIS
    Fills Micro-mgmt work log entries in a running SRE Hub for a date range or from a CSV.

.EXAMPLE
    .\Update-WorkLog.ps1 -From 2026-10-12 -To 2026-10-16 -Summary "Working from office." -SkipWeekends

.EXAMPLE
    .\Update-WorkLog.ps1 -From 2026-10-20 -To 2026-10-21 -Attendance Leave -EmergencyLeave N/A -NetworkPower N/A -Summary "Planned leave"

.EXAMPLE
    .\Update-WorkLog.ps1 -CsvPath .\worklog-fill.csv

    CSV columns: Date,Attendance,EmergencyLeave,NetworkPower,Summary,Escalation,Note
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BaseUrl = 'http://localhost:5280',

    [Parameter(ParameterSetName = 'Range', Mandatory = $true)]
    [datetime]$From,

    [Parameter(ParameterSetName = 'Range', Mandatory = $true)]
    [datetime]$To,

    [Parameter(ParameterSetName = 'Range')]
    [ValidateSet('Yes', 'Late', 'No', 'Leave', 'Comp-off', 'Holiday', 'Weekend', 'DO-wkd', 'DO-wk')]
    [string]$Attendance = 'Yes',

    [Parameter(ParameterSetName = 'Range')]
    [ValidateSet('No', 'Yes', 'Weekend', 'N/A')]
    [string]$EmergencyLeave = 'No',

    [Parameter(ParameterSetName = 'Range')]
    [ValidateSet('No', 'Network issue', 'Power outage', 'Both', 'N/A')]
    [string]$NetworkPower = 'No',

    [Parameter(ParameterSetName = 'Range')]
    [string]$Summary = 'Working from office.',

    [Parameter(ParameterSetName = 'Range')]
    [string]$Escalation = '',

    [Parameter(ParameterSetName = 'Range')]
    [string]$Note = '',

    [Parameter(ParameterSetName = 'Range')]
    [switch]$SkipWeekends,

    [Parameter(ParameterSetName = 'Csv', Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$CsvPath
)

$ErrorActionPreference = 'Stop'

if ($PSCmdlet.ParameterSetName -eq 'Csv') {
    $rows = Import-Csv -Path $CsvPath
}
else {
    if ($To -lt $From) { throw '-To must not be earlier than -From.' }

    $rows = for ($day = $From.Date; $day -le $To.Date; $day = $day.AddDays(1)) {
        if ($SkipWeekends -and $day.DayOfWeek -in 'Saturday', 'Sunday') { continue }

        [pscustomobject]@{
            Date           = $day.ToString('yyyy-MM-dd')
            Attendance     = $Attendance
            EmergencyLeave = $EmergencyLeave
            NetworkPower   = $NetworkPower
            Summary        = $Summary
            Escalation     = $Escalation
            Note           = $Note
        }
    }
}

$credential = Get-Credential -Message 'SRE Hub sign-in (email and password)'
$login = @{ email = $credential.UserName; password = $credential.GetNetworkCredential().Password } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType 'application/json' -Body $login -SessionVariable session | Out-Null

foreach ($row in $rows) {
    $body = @{
        attendance     = $row.Attendance
        emergencyLeave = $row.EmergencyLeave
        networkPower   = $row.NetworkPower
        summary        = $row.Summary
        escalation     = $row.Escalation
        note           = $row.Note
    } | ConvertTo-Json

    if ($PSCmdlet.ShouldProcess($row.Date, "Save $($row.Attendance): $($row.Summary)")) {
        Invoke-RestMethod -Method Put -Uri "$BaseUrl/api/worklog/$($row.Date)" -ContentType 'application/json' -Body $body -WebSession $session | Out-Null
        Write-Host "Saved $($row.Date)"
    }
}
