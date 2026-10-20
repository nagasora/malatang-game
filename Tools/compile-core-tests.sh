#!/bin/zsh
# Core ロジックと EditMode テストだけをコンパイルする（表示層の作業中でもテストの型検査ができるように）。
set -e
ROOT="${0:A:h:h}"
S=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/Resources/Scripting
OUT=/tmp/malatang-core-tests; mkdir -p $OUT
refs=(); for f in $S/Managed/UnityEngine/*.dll; do refs+=("-r:$f"); done
refs+=("-r:$S/NetStandard/ref/2.1.0/netstandard.dll" "-r:$S/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll")
c=(-nologo -noconfig -nostdlib -langversion:9.0 -nowarn:CS0649,CS0414,CS1591 -target:library)
run(){ $S/NetCoreRuntime/dotnet $S/DotNetSdkRoslyn/csc.dll "$@"; }
run $c $refs -out:$OUT/I.dll $ROOT/Assets/MalatangStory/Scripts/Ingredients/*.cs
run $c $refs -r:$OUT/I.dll -out:$OUT/G.dll $ROOT/Assets/MalatangStory/Scripts/Game/Core/*.cs
NUNIT=($ROOT/Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll)
run $c $refs -r:$OUT/I.dll -r:$OUT/G.dll -r:${NUNIT[1]} -r:$ROOT/Library/ScriptAssemblies/UnityEngine.TestRunner.dll -out:$OUT/T.dll $ROOT/Assets/MalatangStory/Tests/EditMode/*.cs
echo COMPILE_OK
