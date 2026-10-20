#!/bin/zsh
# Mac 開発ビルドを作り、自動プレイで主要画面を撮影する。結果は Builds/Verification。
ROOT="${0:A:h:h}"
setopt null_glob
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
if [[ "$1" != "--no-build" ]]; then
  $UNITY -batchmode -nographics -projectPath "$ROOT" -executeMethod MalatangStory.Editor.ProjectBuilder.BuildMac -quit -logFile /tmp/malatang-build-mac.log
  grep -q MALATANG_MAC_BUILD_SUCCESS /tmp/malatang-build-mac.log || { echo BUILD_FAILED; grep -E "error" /tmp/malatang-build-mac.log | head; exit 1; }
fi
OUT="$ROOT/Builds/Verification"
mkdir -p "$OUT"; rm -f "$OUT"/*.png "$OUT"/smoke-*
"$ROOT/Builds/Mac/Malatang Story.app/Contents/MacOS/Malatang Story" --malatang-smoke "$OUT" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile /tmp/malatang-player.log &
PID=$!
for i in $(seq 1 420); do sleep 1; kill -0 $PID 2>/dev/null || break; done
kill $PID 2>/dev/null; wait $PID 2>/dev/null
cat "$OUT/smoke-result.json" 2>/dev/null; echo
cat "$OUT/smoke-failure.txt" 2>/dev/null | head -20
grep -E "^[A-Za-z.]*Exception" /tmp/malatang-player.log | sort | uniq -c | sort -rn | head
