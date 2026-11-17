# -*- coding: utf-8 -*-
"""Extract inline <script> from web/index.html and parse it with esprima (ES5)."""
import io, re, subprocess, sys, urllib.request, os

os.chdir(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# fetch esprima once
if not os.path.exists('tools/esprima.js'):
    urllib.request.urlretrieve(
        'https://unpkg.com/esprima@4.0.1/dist/esprima.js', 'tools/esprima.js')

html = io.open('web/index.html', encoding='utf-8').read()
m = re.search(r'<script>(.*?)</script>', html, re.S)
if not m:
    print('ERROR: no inline script found')
    sys.exit(1)
js = m.group(1)
io.open('tools/ui.js', 'w', encoding='utf-8', newline='\n').write(js)

check = io.open('tools/check-es5.js', 'w', encoding='utf-8', newline='\n')
check.write(
    'var esprima=require("./esprima.js");var fs=require("fs");'
    'var src=fs.readFileSync("tools/ui.js","utf8");'
    'try{esprima.parseScript(src,{ecmaVersion:5});console.log("ES5 parse OK");}'
    'catch(e){console.log("ES5 parse FAIL: "+e.message);process.exit(1);}'
)
check.close()

node = r'C:\Users\cwj1204z\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\node\bin\node.exe'
r = subprocess.run([node, 'tools/check-es5.js'], capture_output=True, text=True)
sys.stdout.write(r.stdout)
if r.returncode != 0:
    sys.stderr.write(r.stderr)
    sys.exit(r.returncode)
