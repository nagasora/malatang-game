#!/bin/zsh
# 指定した C# ファイルだけを Unity の参照付きでコンパイルし、単体でエラーがないかを確認する。
# 使い方: Tools/compile-files.sh path/to/A.cs path/to/B.cs ...
# 並行作業中の他ファイルの未完成部分に影響されずに、自分の担当ファイルを検証するために使う。
set -e
ROOT="${0:A:h:h}"
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents
S=$U/Resources/Scripting
OUT=/tmp/malatang-compile-files-$$
mkdir -p $OUT
refs=()
for f in $S/Managed/UnityEngine/*.dll; do refs+=("-r:$f"); done
refs+=("-r:$S/NetStandard/ref/2.1.0/netstandard.dll" "-r:$S/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll")
for f in UnityEngine.UI Unity.InputSystem Unity.RenderPipelines.Universal.Runtime Unity.RenderPipelines.Core.Runtime Unity.Mathematics; do
  refs+=("-r:$ROOT/Library/ScriptAssemblies/$f.dll")
done
ING=($ROOT/Assets/MalatangStory/Scripts/Ingredients/**/*.cs)
$S/NetCoreRuntime/dotnet $S/DotNetSdkRoslyn/csc.dll -nologo -noconfig -nostdlib -langversion:9.0 -nowarn:CS0649,CS0414,CS1591 -target:library $refs -out:$OUT/Ingredients.dll $ING
$S/NetCoreRuntime/dotnet $S/DotNetSdkRoslyn/csc.dll -nologo -noconfig -nostdlib -langversion:9.0 -nowarn:CS0649,CS0414,CS1591 -target:library $refs -r:$OUT/Ingredients.dll -out:$OUT/Files.dll "$@"
rm -rf $OUT
echo COMPILE_OK
