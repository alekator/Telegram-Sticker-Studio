"""Build a reproducible catalog from a sparse checkout of google/fonts.

The checkout must be at the pinned commit and every selected family must
contain its original OFL license and METADATA.pb. No font is modified.
"""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

REVISION = '06bfe0d62a98375cb09c942fa18e68bd32cff057'

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    source = Path(sys.argv[1]).resolve()
    names = Path(sys.argv[2]).read_text(encoding='utf-8').splitlines()
    output = Path(sys.argv[3])
    commit = subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()
    if commit != REVISION:
        raise ValueError(f'Unexpected source revision: {commit}')
    result = {'revision': REVISION, 'source': 'https://github.com/google/fonts', 'families': []}
    for name in names:
        family = source / name
        if not name.startswith('ofl/') or not family.is_dir():
            raise ValueError(f'Invalid family {name}')
        text = (family / 'METADATA.pb').read_text(encoding='utf-8')
        def field(key):
            found = re.search(r'^' + key + r': "([^"]+)"', text, re.M)
            return found.group(1) if found else ''
        files = []
        for block in re.findall(r'^fonts \{\n(.*?)^\}', text, re.M | re.S):
            def item(key):
                match = re.search(r'^\s*' + key + r': "?([^"\n]+)', block, re.M)
                return match.group(1).strip() if match else ''
            filename = item('filename')
            path = family / filename
            if path.parent != family or not path.is_file():
                raise ValueError(f'Missing or unsafe font path {name}/{filename}')
            files.append({'file': filename, 'style': item('style'), 'weight': int(item('weight') or '400'), 'sha256': digest(path), 'bytes': path.stat().st_size})
        if field('license') != 'OFL' or not files:
            raise ValueError(f'Unexpected metadata: {name}')
        license_file = family / 'OFL.txt'
        result['families'].append({'id': name.split('/')[-1], 'name': field('name'), 'category': field('category'),
            'subsets': re.findall(r'^subsets: "([^"]+)"', text, re.M), 'licenseSha256': digest(license_file), 'fonts': files})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f"{len(result['families'])} families, {sum(len(f['fonts']) for f in result['families'])} font files")

if __name__ == '__main__':
    main()
