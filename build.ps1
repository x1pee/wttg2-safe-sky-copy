param([string]$GamePath = 'W:\Games\Welcome to the Game II', [string]$OutputPath = "$PSScriptRoot\bin")
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GamePath 'WTTG2_Data\Managed'
$core = Join-Path $GamePath 'BepInEx\core'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$references = @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.InputModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$references += '/r:' + (Join-Path $core 'BepInEx.dll')
& $compiler /nologo /noconfig /target:library /platform:anycpu /nostdlib+ /optimize+ ('/out:' + (Join-Path $OutputPath 'WTTG2.SafeSkyCopy.dll')) @references (Join-Path $PSScriptRoot 'src\TextTools.cs') (Join-Path $PSScriptRoot 'src\SafeSkyCopy.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
