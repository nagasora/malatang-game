#!/usr/bin/env python3
"""既存の翻訳を保持し、新しく書かれた日本語の原文だけを追加する。"""
import argparse
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
parser.add_argument('--output', type=Path, default=root / 'Assets/MalatangStory/Resources/Localization/Strings.json')
args = parser.parse_args()
document = json.loads(args.output.read_text())
entries = document['entries']
seen = {entry['ja'] for entry in entries}
base = root / 'Assets/MalatangStory/Scripts/Game'
for folder in ('Core', 'Presentation/UI'):
    for source in sorted((base / folder).glob('*.cs')):
        for token in re.findall(r'"(?:\\.|[^"\\\r\n])*"', source.read_text()):
            try:
                value = json.loads(token)
            except ValueError:
                continue
            if value in seen or not re.search('[\u3040-\u30ff\u3400-\u9fff]', value):
                continue
            seen.add(value)
            entries.append({'key': 'text.' + hashlib.sha256(value.encode()).hexdigest()[:12], 'ja': value, 'en': ''})
entries.sort(key=lambda entry: entry['key'])
keys = [entry['key'] for entry in entries]
if len(keys) != len(set(keys)):
    raise SystemExit('Duplicate localization keys')
result = json.dumps(document, ensure_ascii=False, indent=2) + '\n'
if args.check:
    if args.output.read_text() != result:
        raise SystemExit('Localization source strings changed; run Tools/export-localization.py')
else:
    args.output.write_text(result)
print('LOCALIZATION_OK', len(entries), 'entries')
