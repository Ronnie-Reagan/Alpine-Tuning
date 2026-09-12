param(
    [string]$AssemblyPath = 'C:\Program Files (x86)\Steam\steamapps\common\Sledders\Sledders_Data\Managed\Assembly-CSharp.dll',
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\docs\native-feature-audit.generated.json')
)

$ErrorActionPreference = 'Stop'
$resolvedAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$gameRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $resolvedAssembly))
$cecilPath = Join-Path $gameRoot 'MelonLoader\net35\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecilPath)) {
    throw "Mono.Cecil was not found beside the installed MelonLoader: $cecilPath"
}
Add-Type -Path $cecilPath

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($resolvedAssembly)
try {
    $hash = (Get-FileHash -LiteralPath $resolvedAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    $wanted = @(
        'SnowmobileController', 'SnowmobileStructure', 'SuspensionController', 'RearAxelController',
        'TrackRenderer', 'TrackToSkinnedMesh', 'MeshInterpretter', 'SnowmobilePreviewHelper',
        'FuelStation', 'FuelManager', 'VehicleScriptableObject', 'VehicleListScriptableObject',
        'LevelPropScriptableObject', 'LevelPropSpawner', 'PlayerSelections', 'PlayerManager',
        'Respawnable', 'Controller', 'JLPGEGBOFIP', 'VirtualCamera', 'VirtualCameraManager',
        'CameraFollowerFirstPerson', 'CameraFollowerThirdPerson', 'DroneModeCameraController',
        'DriverStructure', 'DriverGarageController', 'DriverAnamato',
        'ReplayPlayer', 'GhostRaceController', 'RouteManager', 'WeatherManager'
    )
    $records = foreach ($typeName in $wanted) {
        $type = $assembly.MainModule.Types | Where-Object { $_.FullName -eq $typeName -or $_.Name -eq $typeName } | Select-Object -First 1
        if ($null -eq $type) {
            [ordered]@{ type = $typeName; present = $false; fields = @(); methods = @() }
            continue
        }
        [ordered]@{
            type = $typeName
            present = $true
            baseType = if ($type.BaseType) { $type.BaseType.FullName } else { $null }
            fields = @($type.Fields | Sort-Object Name | ForEach-Object {
                [ordered]@{ name = $_.Name; fieldType = $_.FieldType.FullName; static = $_.IsStatic }
            })
            methods = @($type.Methods | Sort-Object Name | ForEach-Object {
                [ordered]@{
                    name = $_.Name
                    returns = $_.ReturnType.FullName
                    parameters = @($_.Parameters | ForEach-Object { $_.ParameterType.FullName })
                }
            })
        }
    }
    $report = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        assemblyPath = $resolvedAssembly
        assemblySha256 = $hash
        assemblyVersion = $assembly.Name.Version.ToString()
        contracts = @($records)
    }
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) | Out-Null
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
    Write-Host "Wrote native feature audit: $resolvedOutput"
    Write-Host "Assembly SHA256: $hash"
}
finally {
    $assembly.Dispose()
}
