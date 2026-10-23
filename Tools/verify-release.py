#!/usr/bin/env python3
"""配布用Macアプリで開発専用の出力が無効なことを実起動して確認する。"""
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

root = Path(__file__).resolve().parents[1]
app = root / 'Builds/MacRelease/Malatang Story.app/Contents/MacOS/Malatang Story'
if not app.is_file():
    raise SystemExit('先に Build Mac (Release) を実行してください。')
output = root / 'Builds/ReleaseVerification' / uuid.uuid4().hex
argument, environment = output / 'argument', output / 'environment'
argument.mkdir(parents=True)
environment.mkdir()
log = output / 'player.log'
process_env = os.environ.copy()
process_env['MALATANG_SMOKE_OUTPUT'] = str(environment)
process = subprocess.Popen([
    str(app), '--malatang-smoke', str(argument), '-screen-width', '1280',
    '-screen-height', '720', '-screen-fullscreen', '0', '-logFile', str(log),
], env=process_env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    time.sleep(14)
    alive = process.poll() is None
finally:
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=20)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
content = log.read_text(errors='replace') if log.exists() else ''
proof = {
    'aliveAfter14Seconds': alive,
    'argumentFiles': len(list(argument.iterdir())),
    'environmentFiles': len(list(environment.iterdir())),
    'prewarmDetailsLogged': '[SoupPrewarm]' in content,
    'exceptionsLogged': 'Exception:' in content,
    'proofDirectory': str(output),
}
passed = alive and proof['argumentFiles'] == proof['environmentFiles'] == 0 and not proof['prewarmDetailsLogged'] and not proof['exceptionsLogged']
proof['result'] = 'passed' if passed else 'failed'
(output.parent / 'release-proof.json').write_text(json.dumps(proof, ensure_ascii=False, indent=2) + '\n')
print(json.dumps(proof, ensure_ascii=False))
raise SystemExit(0 if passed else 1)
