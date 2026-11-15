# mkrepo.py - builds the GitHub repository folder and the release ZIP of FFX Enhanced (no game assets):
#   release\FFX-Enhanced\              repository: README.md, LICENSE, licenses\, sources (same layout as the project)
#   release\FFX_Enhanced_v<ver>.zip    release: game\ (launcher, patcher, EFL), optional\dbghelp.dll, README.md, LICENSE, licenses\
#   python release\mkrepo.py <EFL folder> [--version=0.90-beta]
# The EFL folder holds ffgriever's release files: simpleLog.dll, hook.ini, modules\ff10-file-loader.dll, modules\config\.
import os, sys, shutil, zipfile, re
R = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
args = [x for x in sys.argv[1:] if not x.startswith('--')]
if not args: sys.exit(__doc__ if __doc__ else 'usage: mkrepo.py <EFL folder>')
EFL = os.path.abspath(args[0])
ver = next((x.split('=', 1)[1] for x in sys.argv[1:] if x.startswith('--version=')), '0.90-beta')
REPO = os.path.join(R, 'release', 'FFX-Enhanced')
ZIP = os.path.join(R, 'release', 'FFX_Enhanced_v%s.zip' % ver)
LIC = os.path.join(R, 'release', 'licenses')

TEXT = ('.cs', '.cpp', '.c', '.h', '.py', '.ps1', '.ini', '.txt', '.md', '.def', '.S', '.json', '.gitignore', '.gitattributes')
def crlf(data):
    return data.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')

files = {}                                              # repo path -> source path
def add(src, dst):
    src = os.path.join(R, src)
    if not os.path.isfile(src): sys.exit('missing: ' + src)
    files[dst.replace('\\', '/')] = src
def tree(src, dst, exts=None):
    base = os.path.join(R, src)
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in ('__pycache__', 'bin', 'obj')]
        for f in sorted(fs):
            if exts and not f.endswith(exts): continue
            p = os.path.join(d, f); add(p, os.path.join(dst, os.path.relpath(p, base)))

add(r'release\README.md', 'README.md')
tree(r'launcher\fswrap\src', r'launcher\fswrap\src', ('.cs',))
for f in ('build.ps1', 'gen_advanced.py', 'launcher.ico'): add('launcher\\fswrap\\' + f, 'launcher\\fswrap\\' + f)
tree(r'launcher\fswrap\gen_res', r'launcher\fswrap\gen_res')
for f in ('uwshim/revofx.h', 'gen_dbghelp.py', 'dll/dbghelp.def', 'dll/dbghelp_fwd.S', 'dll/f60_dbghelp.cpp', 'build.py', 'Patcher.cs',
          'f60_host.cpp', 'f60_escmenu.cpp', 'f60_texts.h', 'gen_texts.py', 'ffx_enh.ini', 'rows/VSYNC.cpp', 'rows/PERF-002.cpp', 'rows/X2-VSYNC.cpp'):
    add('fps60exe\\' + f, 'fps60exe\\' + f)
for f in ('ffx_fps60.h', 'ffx_uw.c', 'ffx_uw_data.h'): add('module\\' + f, 'module\\' + f)
tree(r'module\fps60', r'module\fps60')
add(r'tools\vbfbuild.py', r'tools\vbfbuild.py')
add(r'release\mkrepo.py', r'release\mkrepo.py')
# one .md only: the other markdown files become .txt
ren = {k: k[:-3] + '.txt' for k in files if k.endswith('.md') and k != 'README.md'}
for a, b in ren.items(): files[b] = files.pop(a)

if os.path.isdir(REPO):                                 # keep .git, replace the rest
    for e in os.listdir(REPO):
        if e == '.git': continue
        p = os.path.join(REPO, e); shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
os.makedirs(REPO, exist_ok=True)
def put(dst, data):
    p = os.path.join(REPO, dst); os.makedirs(os.path.dirname(p), exist_ok=True)
    if dst.endswith(TEXT) or os.path.basename(dst) in ('LICENSE',):
        for a, b in ren.items(): data = data.replace(os.path.basename(a).encode(), os.path.basename(b).encode())
        data = crlf(data)
    open(p, 'wb').write(data)
for dst, src in files.items(): put(dst, open(src, 'rb').read())
put('LICENSE', open(os.path.join(LIC, 'LICENSE-Apache-2.0.txt'), 'rb').read())
for f in sorted(os.listdir(LIC)): put('licenses/' + f, open(os.path.join(LIC, f), 'rb').read())
put('.gitignore', b'bin/\nobj/\n__pycache__/\n*.log\n')
put('.gitattributes', b'* -text\n')

# privacy / leftovers check
bad = re.compile(rb'Users[\\/]+Tom|scratchpad|nexxus|SteamLibrary|D:[\\/]+Games|E:[\\/]+Games', re.I)
hits = []
for d, _, fs in os.walk(REPO):
    if '.git' in d.split(os.sep): continue
    for f in fs:
        p = os.path.join(d, f); data = open(p, 'rb').read()
        for m in bad.finditer(data): hits.append('%s: %s' % (os.path.relpath(p, REPO), data[max(0, m.start() - 40):m.end() + 40]))
print('repo: %d files' % sum(len(fs) for d, _, fs in os.walk(REPO) if '.git' not in d.split(os.sep)))
for h in hits: print('  CHECK', h)

# release ZIP
items = [(os.path.join(R, r'launcher\fswrap\bin\FFX&X-2_LAUNCHER.exe'), 'game/FFX&X-2_LAUNCHER.exe'),
         (os.path.join(R, r'fps60exe\bin\FFX_enh_Patcher.exe'), 'game/FFX_enh_Patcher.exe'),
         (os.path.join(R, r'fps60exe\bin\dbghelp.dll'), 'optional/dbghelp.dll')]
for f in ('simpleLog.dll', 'hook.ini', r'modules\ff10-file-loader.dll', r'modules\config\ff10-file-loader.ini'):
    items.append((os.path.join(EFL, f), 'game/EFL/' + f.replace('\\', '/')))
for f in ['README.md', 'LICENSE'] + ['licenses/' + x for x in sorted(os.listdir(LIC))]: items.append((os.path.join(REPO, f), f))
with zipfile.ZipFile(ZIP, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for src, dst in items:
        if not os.path.isfile(src): sys.exit('missing: ' + src)
        z.write(src, dst)
with zipfile.ZipFile(ZIP) as z:
    print('%s: %d files, %.1f MB, test %s' % (ZIP, len(z.namelist()), os.path.getsize(ZIP) / 1e6, z.testzip() or 'ok'))
    for i in z.infolist(): print('  %10d  %s' % (i.file_size, i.filename))
