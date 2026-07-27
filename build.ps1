$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compilerCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw "Windows C# compiler was not found."
}

$frameworkDirectory = Split-Path -Parent $compiler
$wpfDirectory = Join-Path $frameworkDirectory "WPF"
$outputDirectory = Join-Path $projectRoot "dist"
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$outputFile = Join-Path $outputDirectory "NuoMi-Desktop-Pet.exe"
$sourceDirectory = Join-Path $projectRoot "src"
$sourceFiles = Get-ChildItem -LiteralPath $sourceDirectory -Filter "*.cs" -File |
    Sort-Object -Property Name
if (-not $sourceFiles) {
    throw "No C# source files were found."
}
$manifestFile = Join-Path $projectRoot "app.manifest"
$rigAssetFile = Join-Path $projectRoot "assets\orange-kitten-rig.png"
$blinkAssetFile = Join-Path $projectRoot "assets\orange-kitten-rig-blink.png"
$bowAssetFile = Join-Path $projectRoot "assets\orange-kitten-bow.png"
if (
    -not (Test-Path -LiteralPath $rigAssetFile) -or
    -not (Test-Path -LiteralPath $blinkAssetFile) -or
    -not (Test-Path -LiteralPath $bowAssetFile)
) {
    throw "Embedded orange kitten rig assets were not found."
}

$intermediateDirectory = Join-Path $projectRoot "obj"
if (-not (Test-Path -LiteralPath $intermediateDirectory)) {
    New-Item -ItemType Directory -Path $intermediateDirectory | Out-Null
}
$iconFile = Join-Path $intermediateDirectory "NuoMi-Desktop-Pet.ico"

Add-Type -AssemblyName System.Drawing
$iconSource = [System.Drawing.Image]::FromFile($rigAssetFile)
$iconSizes = @(16, 32, 48, 256)
$iconImages = New-Object "System.Collections.Generic.List[byte[]]"
try {
    foreach ($iconSize in $iconSizes) {
        $iconBitmap = New-Object System.Drawing.Bitmap $iconSize, $iconSize
        $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
        $pngStream = New-Object System.IO.MemoryStream
        try {
            $iconGraphics.Clear([System.Drawing.Color]::Transparent)
            $iconGraphics.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $iconGraphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $iconGraphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $padding = [Math]::Max(1, [int][Math]::Round($iconSize * 0.025))
            $destination = [System.Drawing.Rectangle]::FromLTRB(
                $padding,
                $padding,
                $iconSize - $padding,
                $iconSize - $padding)
            $iconGraphics.DrawImage(
                $iconSource,
                $destination,
                86,
                100,
                535,
                493,
                [System.Drawing.GraphicsUnit]::Pixel)
            $iconBitmap.Save(
                $pngStream,
                [System.Drawing.Imaging.ImageFormat]::Png)
            $iconImages.Add($pngStream.ToArray())
        }
        finally {
            $pngStream.Dispose()
            $iconGraphics.Dispose()
            $iconBitmap.Dispose()
        }
    }
}
finally {
    $iconSource.Dispose()
}

$iconStream = [System.IO.File]::Create($iconFile)
$iconWriter = New-Object System.IO.BinaryWriter $iconStream
try {
    $iconWriter.Write([UInt16]0)
    $iconWriter.Write([UInt16]1)
    $iconWriter.Write([UInt16]$iconImages.Count)
    $imageOffset = 6 + 16 * $iconImages.Count
    for ($iconIndex = 0; $iconIndex -lt $iconImages.Count; $iconIndex++) {
        $iconSize = $iconSizes[$iconIndex]
        $iconWriter.Write([Byte]$(if ($iconSize -ge 256) { 0 } else { $iconSize }))
        $iconWriter.Write([Byte]$(if ($iconSize -ge 256) { 0 } else { $iconSize }))
        $iconWriter.Write([Byte]0)
        $iconWriter.Write([Byte]0)
        $iconWriter.Write([UInt16]1)
        $iconWriter.Write([UInt16]32)
        $iconWriter.Write([UInt32]$iconImages[$iconIndex].Length)
        $iconWriter.Write([UInt32]$imageOffset)
        $imageOffset += $iconImages[$iconIndex].Length
    }
    foreach ($iconImage in $iconImages) {
        $iconWriter.Write($iconImage)
    }
}
finally {
    $iconWriter.Dispose()
    $iconStream.Dispose()
}

$references = @(
    (Join-Path $frameworkDirectory "System.dll"),
    (Join-Path $frameworkDirectory "System.Core.dll"),
    (Join-Path $frameworkDirectory "System.Drawing.dll"),
    (Join-Path $frameworkDirectory "System.Windows.Forms.dll"),
    (Join-Path $frameworkDirectory "System.Xaml.dll"),
    (Join-Path $wpfDirectory "WindowsBase.dll"),
    (Join-Path $wpfDirectory "PresentationCore.dll"),
    (Join-Path $wpfDirectory "PresentationFramework.dll")
)

$referenceArguments = $references | ForEach-Object { "/reference:`"$_`"" }
$sourceArguments = $sourceFiles | ForEach-Object { "`"$($_.FullName)`"" }
$compilerArguments = @(
    "/nologo",
    "/utf8output",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug-",
    "/win32manifest:`"$manifestFile`"",
    "/win32icon:`"$iconFile`"",
    "/resource:`"$rigAssetFile`",NuoMiDesktopPet.OrangeKittenRig.png",
    "/resource:`"$blinkAssetFile`",NuoMiDesktopPet.OrangeKittenBlink.png",
    "/resource:`"$bowAssetFile`",NuoMiDesktopPet.OrangeKittenBow.png",
    "/out:`"$outputFile`""
) + $referenceArguments + $sourceArguments

& $compiler $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$result = Get-Item -LiteralPath $outputFile
Write-Output ("Created: {0}" -f $result.FullName)
Write-Output ("Size: {0:N0} bytes" -f $result.Length)
