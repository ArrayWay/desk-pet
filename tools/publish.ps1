param(
    [ValidateSet('Debug', 'Release', 'All')]
    [string]$Configuration = 'All'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'desktop-wpf\Fuguang.DesktopPet\Fuguang.DesktopPet.csproj'
$extension = Join-Path $root 'vscode-extension'
$publishRoot = Join-Path $root 'publish'
$configurations = if ($Configuration -eq 'All') { @('Debug', 'Release') } else { @($Configuration) }

foreach ($current in $configurations) {
    $name = $current.ToLowerInvariant()
    $windowsOutput = Join-Path $publishRoot "windows\$name"
    $vscodeOutput = Join-Path $publishRoot "vscode\$name"

    Remove-Item $windowsOutput, $vscodeOutput -Recurse -Force -ErrorAction SilentlyContinue
    New-Item $windowsOutput, $vscodeOutput -ItemType Directory -Force | Out-Null

    dotnet publish $project --configuration $current --output $windowsOutput
    if ($LASTEXITCODE -ne 0) { throw "Windows $current publish failed." }

    Copy-Item (Join-Path $extension '*') $vscodeOutput -Recurse -Force
    Remove-Item (Join-Path $vscodeOutput 'desktop') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item $windowsOutput (Join-Path $vscodeOutput 'desktop') -Recurse -Force

    # Keep the workspace extension desktop runtime in sync so local F5 / source-tree
    # launches do not keep serving a stale bundled Fuguang.DesktopPet.exe.
    if ($current -eq 'Release') {
        $workspaceDesktop = Join-Path $extension 'desktop'
        Remove-Item $workspaceDesktop -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item $windowsOutput $workspaceDesktop -Recurse -Force
    }

    Push-Location $vscodeOutput
    try {
        npm run check
        if ($LASTEXITCODE -ne 0) { throw "VS Code $current validation failed." }
        npx --yes @vscode/vsce package --allow-missing-repository --skip-license --out (Join-Path $vscodeOutput 'fuguang-orange-pet.vsix')
        if ($LASTEXITCODE -ne 0) { throw "VS Code $current package failed." }
    }
    finally {
        Pop-Location
    }
}