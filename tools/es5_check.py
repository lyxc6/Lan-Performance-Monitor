# -*- coding: utf-8 -*-
"""Check that every inline <script> in web/index.html parses as strict ES5.

Why acorn and not esprima: esprima 4 ignores the `ecmaVersion` option and parses
as ES2017 regardless, so arrow functions, `const` and template literals all pass
silently -- it was giving false "ES5 parse OK" results. acorn really does reject
post-ES5 syntax when asked for `ecmaVersion: 5`.

Why all <script> blocks: the page has two. An earlier version of this script only
grabbed the first one (the small head/layout block), so the large UI script was
never checked at all.

Prerequisites are checked explicitly; nothing is downloaded at runtime.
Exit codes: 0 = all blocks parse as ES5, 1 = parse failure or missing prerequisite.
"""
import io
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

PARSER = os.path.join(ROOT, 'tools', 'acorn.js')
OUT_JS = os.path.join(ROOT, 'tools', 'ui.js')
CHECK_JS = os.path.join(ROOT, 'tools', 'check-es5.js')

NODE_HINTS = [r'C:\Program Files\nodejs\node.exe']


def find_node():
    exe = shutil.which('node')
    if exe:
        return exe
    for hint in NODE_HINTS:
        if os.path.exists(hint):
            return hint
    return None


def fail(message):
    # ASCII only: Windows consoles here use a non-UTF-8 codepage, where
    # Chinese text would be printed as mojibake.
    sys.stdout.write(message + '\n')
    sys.exit(1)


def main():
    if not os.path.exists(PARSER):
        fail(
            'ERROR: tools/acorn.js is missing.\n'
            'This script does not download it at runtime. Fetch it once, e.g.:\n'
            '  curl -L -o tools/acorn.js https://unpkg.com/acorn@8.11.3/dist/acorn.js\n'
            '(or download the same URL in a browser and save it as tools/acorn.js)'
        )

    node = find_node()
    if not node:
        fail(
            'ERROR: no node executable found.\n'
            'Install Node.js or add its directory to PATH.\n'
            'This script only checks ES5 syntax and is not needed to build the exe.'
        )

    html = io.open('web/index.html', encoding='utf-8').read()

    # Inline scripts only: <script> ... </script> without a src attribute.
    blocks = re.findall(r'<script>(.*?)</script>', html, re.S)
    if not blocks:
        fail('ERROR: no inline <script> found in web/index.html')

    # Every block goes into one file, separated by explicit markers so a parser
    # error still names which block failed.
    parts = []
    for i, js in enumerate(blocks):
        parts.append('/* === block %d (source line %d) === */' % (i, _line_of(html, i)))
        parts.append(js)
    io.open(OUT_JS, 'w', encoding='utf-8', newline='\n').write('\n'.join(parts))

    checker = (
        'var acorn=require("./acorn.js");var fs=require("fs");'
        'var src=fs.readFileSync("tools/ui.js","utf8");'
        'try{acorn.parse(src,{ecmaVersion:5});console.log("ES5 parse OK");}'
        'catch(e){console.log("ES5 parse FAIL: "+e.message);process.exit(1);}'
    )
    with io.open(CHECK_JS, 'w', encoding='utf-8', newline='\n') as f:
        f.write(checker)

    r = subprocess.run([node, 'tools/check-es5.js'], capture_output=True, text=True)
    sys.stdout.write(r.stdout)
    if r.returncode == 0:
        sys.stdout.write('checked %d inline script block(s) in web/index.html\n' % len(blocks))
    else:
        sys.stderr.write(r.stderr)
    sys.exit(r.returncode)


def _line_of(html, index):
    """Approximate source line of the index-th inline script block."""
    matches = list(re.finditer(r'<script>', html))
    if index < len(matches):
        return html.count('\n', 0, matches[index].start()) + 1
    return 0


if __name__ == '__main__':
    main()
