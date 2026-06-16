param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",

    [string]$OutputRoot = "D:\DabaoV",

    [string]$Version = "",

    [switch]$Clean,

    [switch]$KeepWorkFolder
)

$ErrorActionPreference = "Stop"

function Get-PlatformFromRuntime {
    param([string]$RuntimeIdentifier)

    switch ($RuntimeIdentifier) {
        "win-x64" { return "x64" }
        "win-x86" { return "x86" }
        "win-arm64" { return "ARM64" }
        default { throw "Unsupported runtime: $RuntimeIdentifier" }
    }
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-ProjectProperty {
    param(
        [string]$ProjectPath,
        [string]$PropertyName
    )

    $utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
    [xml]$projectXml = $utf8Strict.GetString([System.IO.File]::ReadAllBytes($ProjectPath))
    foreach ($propertyGroup in $projectXml.Project.PropertyGroup) {
        $value = $propertyGroup.$PropertyName
        if (!([string]::IsNullOrWhiteSpace($value))) {
            return $value.Trim()
        }
    }

    return ""
}

function Get-AppVersion {
    param(
        [string]$ExplicitVersion,
        [string]$ProjectPath
    )

    if (!([string]::IsNullOrWhiteSpace($ExplicitVersion))) {
        return $ExplicitVersion.Trim()
    }

    $projectVersion = Get-ProjectProperty -ProjectPath $ProjectPath -PropertyName "Version"
    if (!([string]::IsNullOrWhiteSpace($projectVersion))) {
        return $projectVersion.Trim()
    }

    throw "App version was not found in $ProjectPath. Pass -Version manually."
}

function Copy-WinUiCompiledResources {
    param(
        [string]$BuildOutputDir,
        [string]$DestinationDir,
        [string]$AppExeBaseName
    )

    $resourceNames = @(
        "$AppExeBaseName.pri",
        "App.xbf",
        "MainWindow.xbf"
    )

    foreach ($resourceName in $resourceNames) {
        $sourcePath = Join-Path $BuildOutputDir $resourceName
        if (!(Test-Path -LiteralPath $sourcePath)) {
            throw "Required WinUI resource was not found: $sourcePath"
        }

        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $DestinationDir $resourceName) -Force
    }
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$Description
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = $Description
    $shortcut.Save()
}

function Copy-SourceToWorkFolder {
    param(
        [string]$SourceRoot,
        [string]$DestinationRoot
    )

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

    $robocopyArgs = @(
        $SourceRoot,
        $DestinationRoot,
        "/E",
        "/XD", ".git", ".vs", ".idea", "bin", "obj",
        "/XF", "*.user",
        "/NFL", "/NDL", "/NJH", "/NJS", "/NP"
    )

    & robocopy @robocopyArgs | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed with exit code $LASTEXITCODE"
    }
}

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot "CrossingVoidZDTool.csproj"
$platform = Get-PlatformFromRuntime -RuntimeIdentifier $Runtime
$targetFramework = Get-ProjectProperty -ProjectPath $projectPath -PropertyName "TargetFramework"
$appDisplayName = Get-ProjectProperty -ProjectPath $projectPath -PropertyName "Product"
$appVersion = Get-AppVersion -ExplicitVersion $Version -ProjectPath $projectPath

if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "TargetFramework was not found in $projectPath."
}

if ([string]::IsNullOrWhiteSpace($appDisplayName)) {
    $appDisplayName = Get-ProjectProperty -ProjectPath $projectPath -PropertyName "AssemblyName"
}

if ([string]::IsNullOrWhiteSpace($appDisplayName)) {
    throw "App display name was not found in $projectPath."
}

$packageBaseName = "$appDisplayName" + "V" + $appVersion
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path "CrossingVoidZDTool-Pakout" "$packageBaseName-$Runtime-$Configuration")
$sourceRoot = Join-Path $workRoot "source"
$publishDir = Join-Path $workRoot "publish"
$stagedProjectPath = Join-Path $sourceRoot "CrossingVoidZDTool.csproj"
$buildOutputDir = Join-Path $sourceRoot (Join-Path "bin" (Join-Path $platform (Join-Path $Configuration (Join-Path $targetFramework $Runtime))))
$packageRoot = Join-Path $OutputRoot $packageBaseName
$programDir = Join-Path $packageRoot $appDisplayName
$shortcutPath = Join-Path $packageRoot "$appDisplayName.lnk"

if (!(Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

if ($Clean) {
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "bin")
    Remove-DirectoryIfExists -Path (Join-Path $repoRoot "obj")
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Remove-DirectoryIfExists -Path $workRoot
Remove-DirectoryIfExists -Path $packageRoot

Write-Host "==> Preparing clean source copy"
Copy-SourceToWorkFolder -SourceRoot $repoRoot -DestinationRoot $sourceRoot

if (!(Test-Path -LiteralPath $stagedProjectPath)) {
    throw "Staged project file not found: $stagedProjectPath"
}

Write-Host "==> Restoring packages"
dotnet restore $stagedProjectPath

Write-Host "==> Publishing $Configuration / $Runtime"
dotnet publish $stagedProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    -p:Platform=$platform `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeHelperExeNames = @(
    "createdump.exe",
    "RestartAgent.exe"
)
$publishedExe = Get-ChildItem -LiteralPath $publishDir -Filter "*.exe" -File |
    Where-Object { $runtimeHelperExeNames -notcontains $_.Name } |
    Sort-Object Length -Descending |
    Select-Object -First 1
if ($null -eq $publishedExe) {
    throw "Publish completed, but no app .exe was found in $publishDir"
}

Write-Host "==> Building release layout"
New-Item -ItemType Directory -Force -Path $programDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $programDir -Recurse -Force
Copy-WinUiCompiledResources `
    -BuildOutputDir $buildOutputDir `
    -DestinationDir $programDir `
    -AppExeBaseName $publishedExe.BaseName

$requiredPaths = @(
    (Join-Path $programDir $publishedExe.Name),
    (Join-Path $programDir "$($publishedExe.BaseName).pri"),
    (Join-Path $programDir "App.xbf"),
    (Join-Path $programDir "MainWindow.xbf"),
    (Join-Path $programDir "Assets\DefaultBuffIcon.png"),
    (Join-Path $programDir "Tools\Unreal\export_zd_assets.py")
)

foreach ($requiredPath in $requiredPaths) {
    if (!(Test-Path -LiteralPath $requiredPath)) {
        throw "Required package file was not found: $requiredPath"
    }
}

$appExe = Join-Path $programDir $publishedExe.Name
New-AppShortcut `
    -ShortcutPath $shortcutPath `
    -TargetPath $appExe `
    -WorkingDirectory $programDir `
    -Description $appDisplayName

if (!$KeepWorkFolder) {
    Remove-DirectoryIfExists -Path $workRoot
}

Write-Host ""
Write-Host "Package folder: $packageRoot"
Write-Host "Version:        $appVersion"
Write-Host "Runtime:        $Runtime"
Write-Host "Program folder: $appDisplayName"
Write-Host "Shortcut:       $appDisplayName.lnk"
if ($KeepWorkFolder) {
    Write-Host "Work folder:    $workRoot"
}
