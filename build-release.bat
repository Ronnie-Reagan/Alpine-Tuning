@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "PUBLIC_VERSION=2026.07.17"
set "ASSEMBLY_VERSION=2026.7.17.0"
for %%I in ("%~dp0.") do set "ROOT=%%~fI"
set "PROJECT=%ROOT%\SleddersTuner\SleddersTuner.csproj"
set "TEST_PROJECT=%ROOT%\ReleaseTests\ReleaseTests.csproj"
set "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Sledders"
set "MODS_DIR=%GAME_DIR%\Mods"
set "GAME_ASSEMBLY=%GAME_DIR%\Sledders_Data\Managed\Assembly-CSharp.dll"
set "LOCAL_RELEASE_DIR=%ROOT%\SleddersTuner\bin\x64\Release"
set "LOCAL_DLL=%LOCAL_RELEASE_DIR%\Alpine Tuning.dll"
set "DEPLOYED_DLL=%MODS_DIR%\Alpine Tuning.dll"
set "STAGE_PARENT=%ProgramData%\AlpineTuning"
set "STAGE_TOKEN="
set "STAGE_ROOT="
set "STAGE_OWNED=0"

call :main
set "BUILD_RESULT=%ERRORLEVEL%"
call :cleanup_stage
set "CLEANUP_RESULT=%ERRORLEVEL%"

if not "%CLEANUP_RESULT%"=="0" (
  echo ERROR: The neutral build staging directory could not be removed.
  exit /b 1
)

if not "%BUILD_RESULT%"=="0" (
  echo ERROR: Alpine Tuning release validation or deployment failed.
  exit /b %BUILD_RESULT%
)

echo.
echo Alpine Tuning %PUBLIC_VERSION% passed the release gate.
echo Built: SleddersTuner\bin\x64\Release\Alpine Tuning.dll
echo Deployed: Sledders\Mods\Alpine Tuning.dll
exit /b 0

:main
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: dotnet was not found.
  exit /b 1
)
where git >nul 2>&1
if errorlevel 1 (
  echo ERROR: git was not found.
  exit /b 1
)
where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: Windows PowerShell was not found.
  exit /b 1
)

if not exist "%PROJECT%" (
  echo ERROR: The Alpine Tuning project file was not found.
  exit /b 1
)
if not exist "%TEST_PROJECT%" (
  echo ERROR: The release regression project was not found.
  exit /b 1
)
if not exist "%GAME_ASSEMBLY%" (
  echo ERROR: The configured Sledders installation is unavailable.
  exit /b 1
)
if not exist "%MODS_DIR%" (
  mkdir "%MODS_DIR%" >nul 2>&1
  if errorlevel 1 (
    echo ERROR: The Sledders Mods directory could not be created.
    exit /b 1
  )
)
if "%ProgramData%"=="" (
  echo ERROR: ProgramData is unavailable for neutral staging.
  exit /b 1
)

call :reserve_stage
if errorlevel 1 (
  echo ERROR: A unique owned neutral staging directory could not be reserved.
  exit /b 1
)

mkdir "%STAGE_SOURCE%" "%STAGE_PAYLOAD%" "%STAGE_BUILD%" "%STAGE_TESTS%" >nul 2>&1
if errorlevel 1 (
  echo ERROR: Neutral release staging could not be created.
  exit /b 1
)
mkdir "%STAGE_ROOT%\temp" "%STAGE_ROOT%\dotnet-home" "%STAGE_ROOT%\nuget" >nul 2>&1
if errorlevel 1 (
  echo ERROR: Neutral tool directories could not be created.
  exit /b 1
)
set "TEMP=%STAGE_ROOT%\temp"
set "TMP=%STAGE_ROOT%\temp"
set "DOTNET_CLI_HOME=%STAGE_ROOT%\dotnet-home"
set "NUGET_PACKAGES=%STAGE_ROOT%\nuget"
set "DOTNET_NOLOGO=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo Checking the public working tree...
git -C "%ROOT%" diff --check
if errorlevel 1 (
  echo ERROR: git diff --check reported whitespace errors.
  exit /b 1
)
git -C "%ROOT%" diff --cached --check
if errorlevel 1 (
  echo ERROR: git diff --cached --check reported whitespace errors.
  exit /b 1
)

git -C "%ROOT%" ls-files --cached --others --exclude-standard > "%INVENTORY%"
if errorlevel 1 (
  echo ERROR: The public-file inventory could not be generated.
  exit /b 1
)
git -C "%ROOT%" ls-files --others --exclude-standard > "%UNTRACKED_INVENTORY%"
if errorlevel 1 (
  echo ERROR: The untracked public-file inventory could not be generated.
  exit /b 1
)

set "ALPINE_SOURCE_ROOT=%ROOT%"
set "ALPINE_UNTRACKED_INVENTORY=%UNTRACKED_INVENTORY%"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $root=[IO.Path]::GetFullPath($env:ALPINE_SOURCE_ROOT); $prefix=$root.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar; $utf8=New-Object Text.UTF8Encoding($false,$true); foreach($relative in [IO.File]::ReadAllLines($env:ALPINE_UNTRACKED_INVENTORY)){if([String]::IsNullOrWhiteSpace($relative)){continue}; $path=[IO.Path]::GetFullPath([IO.Path]::Combine($root,$relative)); if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Untracked inventory escaped the source root.'}; if(-not [IO.File]::Exists($path)){continue}; $name=[IO.Path]::GetFileName($path); $extension=[IO.Path]::GetExtension($path).ToLowerInvariant(); if($name -ne '.gitignore' -and @('.cs','.csproj','.json','.bat','.md','.txt') -notcontains $extension){continue}; $text=$utf8.GetString([IO.File]::ReadAllBytes($path)); if($text.IndexOf([char]0) -ge 0 -or [Text.RegularExpressions.Regex]::IsMatch($text,'(?m)[ 	]+(?=\r?$)') -or [Text.RegularExpressions.Regex]::IsMatch($text,'(?m)^[ 	]* +\t') -or [Text.RegularExpressions.Regex]::IsMatch($text,'(?:\r?\n[ 	]*){2,}$')){throw 'An allowlisted untracked text file failed the whitespace contract.'}}"
if errorlevel 1 (
  echo ERROR: An allowlisted untracked text file contains invalid whitespace.
  exit /b 1
)

set "ALPINE_STAGE_SOURCE=%STAGE_SOURCE%"
set "ALPINE_INVENTORY=%INVENTORY%"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $sourceRoot=[IO.Path]::GetFullPath($env:ALPINE_SOURCE_ROOT); $sourcePrefix=$sourceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar; $targetRoot=[IO.Path]::GetFullPath($env:ALPINE_STAGE_SOURCE); foreach($relative in [IO.File]::ReadAllLines($env:ALPINE_INVENTORY)){ if([String]::IsNullOrWhiteSpace($relative)){continue}; $source=[IO.Path]::GetFullPath([IO.Path]::Combine($sourceRoot,$relative)); if(-not $source.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory path escaped source root.'}; if(-not [IO.File]::Exists($source)){continue}; $target=[IO.Path]::GetFullPath([IO.Path]::Combine($targetRoot,$relative)); if(-not $target.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory path escaped staging root.'}; $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)); [IO.File]::Copy($source,$target,$true) }"
if errorlevel 1 (
  echo ERROR: The allowlisted source could not be copied to neutral staging.
  exit /b 1
)

if not exist "%STAGE_SOURCE%\SleddersTuner\SleddersTuner.csproj" (
  echo ERROR: The staged production project is incomplete.
  exit /b 1
)
if not exist "%STAGE_SOURCE%\ReleaseTests\ReleaseTests.csproj" (
  echo ERROR: The staged release tests are incomplete.
  exit /b 1
)

echo Building Alpine Tuning %PUBLIC_VERSION% in neutral staging...
dotnet msbuild "%STAGE_SOURCE%\SleddersTuner\SleddersTuner.csproj" -t:Rebuild -p:Configuration=Release -p:Platform=x64 -p:OutDir="%STAGE_PAYLOAD%\\" -p:IntermediateOutputPath="%STAGE_BUILD%\production\\" -p:DebugSymbols=false -p:DebugType=None -p:Deterministic=true -p:SleddersDir="%GAME_DIR%" -nologo -verbosity:minimal
if errorlevel 1 (
  echo ERROR: The Release/x64 production build failed.
  exit /b 1
)

echo Building the dependency-free release regression runner...
dotnet msbuild "%STAGE_SOURCE%\ReleaseTests\ReleaseTests.csproj" -t:Rebuild -p:Configuration=Release -p:Platform=x64 -p:OutDir="%STAGE_TESTS%\\" -p:IntermediateOutputPath="%STAGE_BUILD%\tests\\" -p:DebugSymbols=false -p:DebugType=None -p:Deterministic=true -p:AlpineTuningAssemblyPath="%STAGED_DLL%" -p:SleddersDir="%GAME_DIR%" -nologo -verbosity:minimal
if errorlevel 1 (
  echo ERROR: The release regression runner build failed.
  exit /b 1
)

if not exist "%STAGED_DLL%" (
  echo ERROR: The expected release DLL was not produced.
  exit /b 1
)
for /f %%N in ('dir /b /a-d "%STAGE_PAYLOAD%" 2^>nul ^| find /c /v ""') do set "PAYLOAD_COUNT=%%N"
if not "%PAYLOAD_COUNT%"=="1" (
  echo ERROR: The release payload contains unexpected files.
  exit /b 1
)
for /f "delims=" %%F in ('dir /b /a-d "%STAGE_PAYLOAD%" 2^>nul') do if /I not "%%F"=="Alpine Tuning.dll" (
  echo ERROR: The release payload contains an unexpected file type.
  exit /b 1
)
dir /s /b "%STAGE_ROOT%\*.pdb" >nul 2>&1
if not errorlevel 1 (
  echo ERROR: A PDB was generated during the release build.
  exit /b 1
)
dir /s /b "%STAGE_ROOT%\*.binlog" >nul 2>&1
if not errorlevel 1 (
  echo ERROR: A binlog was generated during the release build.
  exit /b 1
)

echo Running release regression, native-contract, asset, inventory, and privacy checks...
pushd "%STAGE_ROOT%" >nul
if errorlevel 1 (
  echo ERROR: The neutral test working directory is unavailable.
  exit /b 1
)
"%STAGE_TESTS%\AlpineTuning.ReleaseTests.exe" --repo "%STAGE_SOURCE%" --assembly "%STAGED_DLL%" --game-assembly "%GAME_ASSEMBLY%" --inventory "%INVENTORY%" --scan-root "%STAGE_ROOT%" --tune-test-root "%STAGE_ROOT%\tune-fixtures"
set "TEST_RESULT=%ERRORLEVEL%"
popd >nul
if not "%TEST_RESULT%"=="0" (
  echo ERROR: The release regression gate failed.
  exit /b 1
)

if not exist "%LOCAL_RELEASE_DIR%" mkdir "%LOCAL_RELEASE_DIR%" >nul 2>&1
if not exist "%LOCAL_RELEASE_DIR%" (
  echo ERROR: The local Release directory could not be created.
  exit /b 1
)

call :delete_file_checked "%LOCAL_RELEASE_DIR%\Alpine Tuning.pdb"
if errorlevel 1 (
  echo ERROR: A stale local Release PDB could not be removed.
  exit /b 1
)
call :delete_file_checked "%MODS_DIR%\Alpine Tuning.pdb"
if errorlevel 1 (
  echo ERROR: A stale deployed PDB could not be removed.
  exit /b 1
)

call :transactional_deploy
if errorlevel 1 (
  echo ERROR: The verified DLL pair could not be installed and validated transactionally.
  exit /b 1
)

exit /b 0

:reserve_stage
set "ALPINE_STAGE_PARENT=%STAGE_PARENT%"
for /f "usebackq delims=" %%G in (`powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $parent=[IO.Path]::GetFullPath($env:ALPINE_STAGE_PARENT); $null=[IO.Directory]::CreateDirectory($parent); for($attempt=0;$attempt -lt 32;$attempt++){ $token=[Guid]::NewGuid().ToString('N'); $stage=[IO.Path]::Combine($parent,'release-'+$token); $temporary=[IO.Path]::Combine($parent,'.alpine-stage-'+$token+'.tmp'); if([IO.Directory]::Exists($stage) -or [IO.Directory]::Exists($temporary)){continue}; try { $null=[IO.Directory]::CreateDirectory($temporary); $marker=[IO.Path]::Combine($temporary,'.alpine-stage-owner'); [IO.File]::WriteAllText($marker,$token,(New-Object Text.UTF8Encoding($false))); [IO.Directory]::Move($temporary,$stage); [Console]::Out.WriteLine($token); exit 0 } catch { if([IO.Directory]::Exists($temporary)){ $marker=[IO.Path]::Combine($temporary,'.alpine-stage-owner'); if([IO.File]::Exists($marker) -and [IO.File]::ReadAllText($marker).Trim() -ceq $token){[IO.Directory]::Delete($temporary,$true)} } } }; exit 3"`) do if not defined STAGE_TOKEN set "STAGE_TOKEN=%%G"
if not defined STAGE_TOKEN exit /b 1
set "STAGE_ROOT=%STAGE_PARENT%\release-%STAGE_TOKEN%"
set "STAGE_SOURCE=%STAGE_ROOT%\src"
set "STAGE_PAYLOAD=%STAGE_ROOT%\payload"
set "STAGE_BUILD=%STAGE_ROOT%\obj"
set "STAGE_TESTS=%STAGE_ROOT%\tests"
set "INVENTORY=%STAGE_ROOT%\public-files.txt"
set "UNTRACKED_INVENTORY=%STAGE_ROOT%\untracked-public-files.txt"
set "STAGED_DLL=%STAGE_PAYLOAD%\Alpine Tuning.dll"
set "STAGE_OWNED=1"
set "ALPINE_STAGE_ROOT=%STAGE_ROOT%"
set "ALPINE_STAGE_TOKEN=%STAGE_TOKEN%"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $stage=[IO.Path]::GetFullPath($env:ALPINE_STAGE_ROOT); $parent=[IO.Path]::GetFullPath($env:ALPINE_STAGE_PARENT); $expected=[IO.Path]::Combine($parent,'release-'+$env:ALPINE_STAGE_TOKEN); $marker=[IO.Path]::Combine($stage,'.alpine-stage-owner'); if($stage -cne $expected -or -not [IO.Directory]::Exists($stage) -or -not [IO.File]::Exists($marker) -or [IO.File]::ReadAllText($marker).Trim() -cne $env:ALPINE_STAGE_TOKEN){exit 3}"
exit /b %ERRORLEVEL%

:transactional_deploy
set "ALPINE_DEPLOY_SOURCE=%STAGED_DLL%"
set "ALPINE_DEPLOY_LOCAL=%LOCAL_DLL%"
set "ALPINE_DEPLOY_GAME=%DEPLOYED_DLL%"
set "ALPINE_DEPLOY_BACKUPS=%STAGE_ROOT%\deployment-backups"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; function Get-Hash([string]$path){$sha=[Security.Cryptography.SHA256]::Create(); try {$stream=[IO.File]::OpenRead($path); try {return [Convert]::ToBase64String($sha.ComputeHash($stream))} finally {$stream.Dispose()}} finally {$sha.Dispose()}}; function Install-Atomic([string]$source,[string]$destination){$directory=[IO.Path]::GetDirectoryName($destination); $null=[IO.Directory]::CreateDirectory($directory); $token=[Guid]::NewGuid().ToString('N'); $temporary=[IO.Path]::Combine($directory,'.alpine-release-'+$token+'.tmp'); $replaceBackup=[IO.Path]::Combine($directory,'.alpine-release-'+$token+'.bak'); try {[IO.File]::Copy($source,$temporary,$false); if([IO.File]::Exists($destination)){[IO.File]::Replace($temporary,$destination,$replaceBackup,$true)} else {[IO.File]::Move($temporary,$destination)}} finally {if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}; if([IO.File]::Exists($replaceBackup)){[IO.File]::Delete($replaceBackup)}}}; $source=[IO.Path]::GetFullPath($env:ALPINE_DEPLOY_SOURCE); $backupRoot=[IO.Path]::GetFullPath($env:ALPINE_DEPLOY_BACKUPS); $states=@([pscustomobject]@{Destination=[IO.Path]::GetFullPath($env:ALPINE_DEPLOY_LOCAL);Backup=[IO.Path]::Combine($backupRoot,'local.previous');Existed=$false;OriginalHash=$null},[pscustomobject]@{Destination=[IO.Path]::GetFullPath($env:ALPINE_DEPLOY_GAME);Backup=[IO.Path]::Combine($backupRoot,'game.previous');Existed=$false;OriginalHash=$null}); $mutated=$false; try {if(-not [IO.File]::Exists($source)){throw 'Missing source.'}; if([IO.Directory]::Exists($backupRoot)){throw 'Backup collision.'}; $null=[IO.Directory]::CreateDirectory($backupRoot); foreach($state in $states){$state.Existed=[IO.File]::Exists($state.Destination); if($state.Existed){$state.OriginalHash=Get-Hash $state.Destination; [IO.File]::Copy($state.Destination,$state.Backup,$false); if((Get-Hash $state.Backup) -cne $state.OriginalHash){throw 'Backup verification failed.'}}}; $expected=Get-Hash $source; foreach($state in $states){$mutated=$true; Install-Atomic $source $state.Destination}; foreach($state in $states){if(-not [IO.File]::Exists($state.Destination) -or (Get-Hash $state.Destination) -cne $expected){throw 'Installed hash mismatch.'}}; exit 0} catch {if(-not $mutated){exit 3}; $rollbackOk=$true; foreach($state in $states){try {if($state.Existed){if(-not [IO.File]::Exists($state.Backup)){throw 'Missing rollback payload.'}; Install-Atomic $state.Backup $state.Destination; if((Get-Hash $state.Destination) -cne $state.OriginalHash){throw 'Rollback hash mismatch.'}} else {if([IO.File]::Exists($state.Destination)){[IO.File]::Delete($state.Destination)}; if([IO.File]::Exists($state.Destination)){throw 'Rollback deletion failed.'}}} catch {$rollbackOk=$false}}; if($rollbackOk){exit 3}else{exit 4}}"
exit /b %ERRORLEVEL%

:delete_file_checked
set "ALPINE_DELETE_PATH=%~1"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $path=[IO.Path]::GetFullPath($env:ALPINE_DELETE_PATH); if([IO.File]::Exists($path)){[IO.File]::Delete($path)}; if([IO.File]::Exists($path)){exit 3}"
exit /b %ERRORLEVEL%

:cleanup_stage
if not "%STAGE_OWNED%"=="1" exit /b 0
set "ALPINE_STAGE_ROOT=%STAGE_ROOT%"
powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $stage=[IO.Path]::GetFullPath($env:ALPINE_STAGE_ROOT); $parent=[IO.Path]::GetFullPath($env:ALPINE_STAGE_PARENT); $expected=[IO.Path]::Combine($parent,'release-'+$env:ALPINE_STAGE_TOKEN); $marker=[IO.Path]::Combine($stage,'.alpine-stage-owner'); if($stage -cne $expected){exit 2}; if(-not [IO.Directory]::Exists($stage)){exit 0}; if(-not [IO.File]::Exists($marker) -or [IO.File]::ReadAllText($marker).Trim() -cne $env:ALPINE_STAGE_TOKEN){exit 3}; [IO.Directory]::Delete($stage,$true); if([IO.Directory]::Exists($stage)){exit 4}"
exit /b %ERRORLEVEL%
