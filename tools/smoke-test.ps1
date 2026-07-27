param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\publish\windows\release')
)

$ErrorActionPreference = 'Stop'
$publishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$executable = Join-Path $publishDirectory 'Fuguang.DesktopPet.exe'
$animationPath = Join-Path $publishDirectory 'Assets\pet-animation.json'
$extensionManifestPath = Join-Path $PSScriptRoot '..\vscode-extension\package.json'
$settingsPath = Join-Path $publishDirectory 'Data\pet-settings.json'
$originalSettings = if (Test-Path $settingsPath) { [System.IO.File]::ReadAllBytes($settingsPath) } else { $null }

if (-not (Test-Path $executable)) { throw "Missing desktop executable: $executable" }
if (-not (Test-Path $animationPath)) { throw "Missing animation config: $animationPath" }
if (-not (Test-Path (Join-Path $publishDirectory 'Assets\spritesheet.png'))) { throw 'Missing PNG spritesheet.' }

$companionDirectory = Join-Path $publishDirectory 'Assets\Companions'
foreach ($relativePath in @('dog.png', 'dog\ball.png')) {
    if (-not (Test-Path (Join-Path $companionDirectory $relativePath))) { throw "Missing visitor asset: $relativePath" }
}
foreach ($stateName in @('idle', 'running-right', 'running-left', 'sitting', 'lying-down', 'sleeping', 'waking-stretch', 'happy-celebration', 'sad', 'petting-response', 'confused-dodge', 'waiting', 'guarding', 'comforting', 'sniffing-right', 'peeking', 'carrying-ball-right', 'carrying-ball-left')) {
    $stateDirectory = Join-Path $companionDirectory "dog\$stateName"
    if (-not (Test-Path $stateDirectory)) { throw "Missing visitor state assets: $stateName" }
    if (@(Get-ChildItem $stateDirectory -Filter '*.png' -File).Count -eq 0) { throw "Missing visitor state PNG frames: $stateName" }
}
foreach ($relativePath in @('training-dog\training-dog.png', 'training-dog\food-bowl.png', 'training-dog\frisbee.png')) {
    if (-not (Test-Path (Join-Path $companionDirectory $relativePath))) { throw "Missing training dog asset: $relativePath" }
}
foreach ($stateName in @('running-right', 'running-left', 'half-sit-panting', 'handshake-offer', 'handshake-success', 'handshake-spin', 'food-sniff', 'eating', 'licking-thanks', 'treat-eating', 'frisbee-watch', 'frisbee-run-right', 'frisbee-run-left', 'frisbee-catch-right', 'frisbee-catch-left', 'frisbee-landing', 'frisbee-return-right', 'frisbee-return-left', 'frisbee-miss', 'frisbee-showoff-with-disc')) {
    $stateDirectory = Join-Path $companionDirectory "training-dog\$stateName"
    if (-not (Test-Path $stateDirectory)) { throw "Missing training dog state assets: $stateName" }
    if (@(Get-ChildItem $stateDirectory -Filter '*.png' -File).Count -ne 6) { throw "Training dog state must contain 6 PNG frames: $stateName" }
}

$animation = Get-Content $animationPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($animation.spritesheet.cellWidth -le 0 -or $animation.spritesheet.cellHeight -le 0) { throw 'Invalid spritesheet cell size.' }
foreach ($stateName in @('idle', 'running-right', 'running-left', 'waving', 'jumping', 'failed', 'waiting', 'running', 'review', 'picked-up', 'landing', 'stretching', 'sitting', 'sleeping', 'celebrating')) {
    if (-not $animation.states.PSObject.Properties[$stateName]) { throw "Missing animation state: $stateName" }
}

$manifest = Get-Content $extensionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$commands = @($manifest.contributes.commands.command)
foreach ($command in @('fuguangPet.showDesktop', 'fuguangPet.hideDesktop', 'fuguangPet.togglePause', 'fuguangPet.exitDesktop', 'fuguangPet.startFocus', 'fuguangPet.remindLater')) {
    if ($commands -notcontains $command) { throw "Missing extension command: $command" }
}

function Send-PetMessage([hashtable]$Message) {
    $client = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'fuguang-desktop-pet', [System.IO.Pipes.PipeDirection]::Out)
    try {
        $client.Connect(3000)
        $writer = [System.IO.StreamWriter]::new($client)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine(($Message | ConvertTo-Json -Compress))
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

[System.IO.Directory]::CreateDirectory((Split-Path $settingsPath -Parent)) | Out-Null
[System.IO.File]::WriteAllText($settingsPath, '{"Visitor":{"ActiveVisitorId":"training-dog","Enabled":true}}')

$process = Start-Process -FilePath $executable -WorkingDirectory $publishDirectory -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        try {
            Send-PetMessage @{ command = 'show' }
            $ready = $true
        }
        catch [System.TimeoutException] {
            $ready = $false
        }
    } while (-not $ready -and [DateTime]::UtcNow -lt $deadline)
    if (-not $ready) { throw 'Desktop pet named pipe did not become ready.' }

    Send-PetMessage @{ command = 'hide' }
    Send-PetMessage @{ command = 'toggle-pause' }
    Send-PetMessage @{ command = 'toggle-pause' }
    Send-PetMessage @{ command = 'show' }
    $secondProcess = Start-Process -FilePath $executable -WorkingDirectory $publishDirectory -PassThru
    $secondProcess.WaitForExit(3000) | Out-Null
    if (-not $secondProcess.HasExited) { throw 'Second desktop process did not converge to the existing instance.' }
    $matchingProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $executable })
    if ($matchingProcesses.Count -ne 1) { throw "Expected one desktop process, found $($matchingProcesses.Count)." }

    Send-PetMessage @{ command = 'exit' }
    $process.WaitForExit(5000) | Out-Null
    if (-not $process.HasExited) { throw 'Desktop pet did not exit after the exit command.' }

    $settings = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $settings.Visitor -or $settings.Visitor.ActiveVisitorId -ne 'training-dog') { throw 'Training dog visitor settings were not preserved.' }
    if (@($settings.PSObject.Properties.Name | Where-Object { $_ -like 'DogCompanion*' }).Count -ne 0) { throw 'Legacy dog companion settings were persisted.' }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($null -ne $originalSettings) {
        [System.IO.File]::WriteAllBytes($settingsPath, $originalSettings)
    }
    else {
        Remove-Item $settingsPath -Force -ErrorAction SilentlyContinue
        $dataDirectory = Split-Path $settingsPath -Parent
        if ((Test-Path $dataDirectory) -and -not (Get-ChildItem $dataDirectory -Force)) {
            Remove-Item $dataDirectory -Force
        }
    }
}

Write-Host 'Smoke test passed.'