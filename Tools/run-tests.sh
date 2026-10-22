#!/bin/zsh
# 古いXMLを成功結果と誤認しないよう、今回の終了コードと新規結果を必ず確認する。
set -eu
ROOT="${0:A:h:h}"
MODE=${1:-EditMode}
[[ "$MODE" == EditMode || "$MODE" == PlayMode ]] || { echo 'Use EditMode or PlayMode' >&2; exit 2; }
LOWER=${MODE:l}
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
mkdir -p "$ROOT/TestResults"
RESULT="$ROOT/TestResults/$LOWER-$(date +%Y%m%d-%H%M%S)-$$.xml"
EXTRA=()
[[ $MODE == EditMode ]] && EXTRA=(-nographics)
status_code=0
$UNITY -batchmode $EXTRA -projectPath "$ROOT" -runTests -testPlatform "$MODE" -testResults "$RESULT" -logFile /tmp/malatang-$LOWER.log || status_code=$?
echo "EXIT=$status_code"
python3 - "$RESULT" "$status_code" <<'P'
import sys, xml.etree.ElementTree as ET
try:
    r = ET.parse(sys.argv[1]).getroot()
except (OSError, ET.ParseError) as e:
    print('今回の結果ファイルなし:', e)
    sys.exit(1)
print('total', r.get('total'), 'passed', r.get('passed'), 'failed', r.get('failed'))
for tc in r.iter('test-case'):
    if tc.get('result') != 'Passed':
        print(tc.get('result'), tc.get('name'))
        print(tc.findtext('.//message', '')[:1500])
        print(tc.findtext('.//stack-trace', '')[:1200])
if int(sys.argv[2]) or r.get('result') != 'Passed' or int(r.get('passed', '0')) == 0:
    sys.exit(1)
P
cp "$RESULT" "$ROOT/TestResults/$LOWER.xml"
