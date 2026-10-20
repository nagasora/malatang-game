#!/bin/zsh
# Unity のテストを実行し、結果の要約と失敗内容・実行時例外を表示する。
# 使い方: Tools/run-tests.sh EditMode|PlayMode
ROOT="${0:A:h:h}"
MODE=${1:-EditMode}
LOWER=${MODE:l}
UNITY=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
EXTRA=()
[[ $MODE == EditMode ]] && EXTRA=(-nographics)
$UNITY -batchmode $EXTRA -projectPath "$ROOT" -runTests -testPlatform $MODE -testResults "$ROOT/TestResults/$LOWER.xml" -logFile /tmp/malatang-$LOWER.log
echo "EXIT=$?"
python3 - "$ROOT/TestResults/$LOWER.xml" <<'P'
import sys, xml.etree.ElementTree as ET
try: r = ET.parse(sys.argv[1]).getroot()
except Exception as e: print("結果ファイルなし", e); sys.exit()
print("total", r.attrib.get('total'), "passed", r.attrib.get('passed'), "failed", r.attrib.get('failed'))
for tc in r.iter('test-case'):
    if tc.attrib.get('result') != 'Passed':
        print("FAIL", tc.attrib['name'])
        m = tc.find('.//message'); st = tc.find('.//stack-trace')
        print((m.text or '')[:1500] if m is not None else '')
        print((st.text or '')[:1200] if st is not None else '')
P
grep -E "^[A-Za-z.]*Exception" /tmp/malatang-$LOWER.log | sort | uniq -c | sort -rn | head -10
