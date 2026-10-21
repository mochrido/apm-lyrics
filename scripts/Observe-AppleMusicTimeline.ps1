<#
.SYNOPSIS
    Passively records the Apple Music session's Windows media timeline.
.DESCRIPTION
    Polls GlobalSystemMediaTransportControlsSessionTimelineProperties every
    PollMilliseconds and appends one JSON object per observation to a JSONL
    file. It never sends transport commands, never changes the session, and
    never reads tokens or credentials. Used to measure how Apple Music
    reports position, LastUpdatedTime, duration, and playback state during
    normal playback, pause/resume, seeks, and track switches.

    The output path is required so recordings never land in the repository.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Observe-AppleMusicTimeline.ps1 `
        -Seconds 90 -OutputPath "$env:TEMP\apm-timeline.jsonl"
#>
param(
    [ValidateRange(1, 3600)]
    [int] $Seconds = 60,

    [string] $OutputPath,

    [ValidateRange(50, 5000)]
    [int] $PollMilliseconds = 250,

    [string] $SessionMarker = 'AppleMusicWin'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    throw 'OutputPath is required; recordings must not be written into the repository.'
}

$logPath = [System.IO.Path]::GetFullPath($OutputPath)
$logDir = [System.IO.Path]::GetDirectoryName($logPath)
if ($logDir -and -not (Test-Path -LiteralPath $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]

function Await-WinRt($operation, [Type] $resultType) {
    $task = $asTaskGeneric.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.Wait(10000) | Out-Null
    if ($task.IsFaulted) { throw $task.Exception.InnerException }
    if (-not $task.IsCompleted) { return $null }
    return $task.Result
}

$managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]

$manager = Await-WinRt ($managerType::RequestAsync()) $managerType
if ($null -eq $manager) {
    throw 'Could not open the Windows media session manager.'
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$deadline = [TimeSpan]::FromSeconds($Seconds)
$wrote = 0
$lastError = $null

Write-Host "Recording Apple Music timeline for $Seconds s to $logPath (Ctrl+C to stop early)."

while ($sw.Elapsed -lt $deadline) {
    $observedAtUtc = [DateTimeOffset]::UtcNow
    $observedAtMonotonicMs = [long]$sw.Elapsed.TotalMilliseconds

    $record = [ordered]@{
        observedAtUtc          = $observedAtUtc.ToString('o')
        observedAtMonotonicMs  = $observedAtMonotonicMs
        sessionFound           = $false
        sessionId              = $null
        sourceAppUserModelId   = $null
        title                  = $null
        artist                 = $null
        album                  = $null
        playbackStatus         = $null
        playbackRate           = $null
        positionSeconds        = $null
        startSeconds           = $null
        endSeconds             = $null
        minSeekSeconds         = $null
        maxSeekSeconds         = $null
        lastUpdatedTimeUtc     = $null
        error                  = $null
    }

    try {
        $sessions = $manager.GetSessions()
        $session = $null
        foreach ($s in $sessions) {
            if ($s.SourceAppUserModelId -and
                $s.SourceAppUserModelId.IndexOf($SessionMarker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $session = $s
                break
            }
        }

        if ($null -eq $session) {
            $record.sessionFound = $false
        }
        else {
            $record.sessionFound = $true
            $record.sessionId = $session.SessionId
            $record.sourceAppUserModelId = $session.SourceAppUserModelId

            $props = Await-WinRt ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
            $timeline = $session.GetTimelineProperties()
            $playback = $session.GetPlaybackInfo()

            if ($null -ne $props) {
                $record.title = $props.Title
                $record.artist = $props.Artist
                $record.album = $props.AlbumTitle
            }

            $record.playbackStatus = [string]$playback.PlaybackStatus
            $record.playbackRate = $playback.PlaybackRate
            $record.positionSeconds = $timeline.Position.TotalSeconds
            $record.startSeconds = $timeline.StartTime.TotalSeconds
            $record.endSeconds = $timeline.EndTime.TotalSeconds
            $record.minSeekSeconds = $timeline.MinSeekTime.TotalSeconds
            $record.maxSeekSeconds = $timeline.MaxSeekTime.TotalSeconds
            $record.lastUpdatedTimeUtc = $timeline.LastUpdatedTime.ToString('o')
        }
    }
    catch {
        $lastError = $_.Exception.Message
        $record.error = $lastError
    }

    ($record | ConvertTo-Json -Compress -Depth 3) |
        Out-File -LiteralPath $logPath -Append -Encoding utf8
    $wrote++

    Start-Sleep -Milliseconds $PollMilliseconds
}

Write-Host "Wrote $wrote observations to $logPath"
if ($lastError) {
    Write-Host "Last error (recorded in the file): $lastError"
}
