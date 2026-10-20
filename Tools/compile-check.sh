#!/bin/zsh
# Unity を起動せずに、同梱 Roslyn でスクリプトの構文・型エラーだけを素早く確認する。
# 本番のコンパイルは Unity が行う（定義シンボルや ILPP はここでは再現しない）。
set -e
ROOT="${0:A:h:h}"
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents
S=$U/Resources/Scripting
DOTNET=$S/NetCoreRuntime/dotnet
CSC=$S/DotNetSdkRoslyn/csc.dll
OUT=/tmp/malatang-compile
mkdir -p $OUT
refs=()
for f in $S/Managed/UnityEngine/*.dll; do refs+=("-r:$f"); done
refs+=("-r:$S/NetStandard/ref/2.1.0/netstandard.dll" "-r:$S/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll")
for f in UnityEngine.UI Unity.InputSystem Unity.RenderPipelines.Universal.Runtime Unity.RenderPipelines.Core.Runtime Unity.Mathematics; do
  refs+=("-r:$ROOT/Library/ScriptAssemblies/$f.dll")
done
common=(-nologo -noconfig -nostdlib -langversion:9.0 -nowarn:CS0649,CS0414,CS1591 -target:library -define:UNITY_EDITOR -define:DEVELOPMENT_BUILD)
ING=($ROOT/Assets/MalatangStory/Scripts/Ingredients/**/*.cs)
$DOTNET $CSC $common $refs -out:$OUT/MalatangStory.Ingredients.dll $ING
GAME=($ROOT/Assets/MalatangStory/Scripts/Game/**/*.cs)
$DOTNET $CSC $common $refs -r:$OUT/MalatangStory.Ingredients.dll -out:$OUT/MalatangStory.Game.dll $GAME
NUNIT=($ROOT/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll)
TR=$ROOT/Library/ScriptAssemblies/UnityEngine.TestRunner.dll
$DOTNET $CSC $common $refs -r:$OUT/MalatangStory.Ingredients.dll -r:$OUT/MalatangStory.Game.dll -r:${NUNIT[1]} -r:$TR -out:$OUT/Tests.EditMode.dll $ROOT/Assets/MalatangStory/Tests/EditMode/*.cs
$DOTNET $CSC $common $refs -r:$OUT/MalatangStory.Ingredients.dll -r:$OUT/MalatangStory.Game.dll -r:${NUNIT[1]} -r:$TR -out:$OUT/Tests.PlayMode.dll $ROOT/Assets/MalatangStory/Tests/PlayMode/*.cs
echo COMPILE_OK
