#!/usr/bin/env python3
# build.py - builds bin\FFX_enh_Patcher.exe (the patcher that writes FFX_enh.exe / FFX-2_enh.exe from the user's own
# FFX.exe / FFX-2.exe, Patcher.cs) and bin\dbghelp.dll (the optional DLL build of the same patch, run gen_dbghelp.py first).
# docs\FPS60_EXE.md has the design.
#   python build.py [--rebuild]
# Needs: python package ziglang (compiler, as RevoFX's build) and the csc.exe of the .NET Framework 4 (part of Windows).
# The patch code (..\module\fps60 + f60_host.cpp) is compiled and linked as one relocatable PE image, obj\f60.img, which is
# embedded in the patcher as a resource together with the default ffx_enh.ini. No game file is read by the build.
import os, subprocess, sys, concurrent.futures as cf

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.normpath(os.path.join(HERE, '..', 'module'))
OBJ = os.path.join(HERE, 'obj')
FLAGS = ['-target', 'x86-windows-gnu', '-O2', '-std=c++17', '-DF60_EXE', '-fasm-blocks', '-fno-strict-aliasing', '-fwrapv', '-fno-delete-null-pointer-checks', '-w']
CFLAGS = ['-target', 'x86-windows-gnu', '-O2', '-w']
FOLDERS = ['fps60', 'fps60/x', 'fps60/x/rows', 'fps60/x2', 'fps60/x2/rows', 'fps60/minhook', 'fps60/minhook/hde']

def zig(*a):
    r = subprocess.run([sys.executable, '-m', 'ziglang', *a], capture_output=True, text=True)
    if r.returncode: sys.exit('zig %s failed:\n%s' % (' '.join(a[:1]), r.stdout + r.stderr))

def compile_all(rebuild):
    os.makedirs(OBJ, exist_ok=True)
    jobs = [(os.path.join(HERE, f), os.path.join(OBJ, os.path.splitext(f)[0] + '.o')) for f in sorted(os.listdir(HERE)) if f.endswith('.cpp')]   # f60_host.cpp, f60_escmenu.cpp
    jobs += [(os.path.join(HERE, 'rows', f), os.path.join(OBJ, 'exerows_' + os.path.splitext(f)[0] + '.o')) for f in sorted(os.listdir(os.path.join(HERE, 'rows'))) if f.endswith('.cpp')]   # rows the RevoFX module leaves out
    for d in FOLDERS:
        for f in sorted(os.listdir(os.path.join(MOD, d))):
            if f.endswith(('.c', '.cpp')): jobs.append((os.path.join(MOD, d, f), os.path.join(OBJ, d.replace('/', '_') + '_' + os.path.splitext(f)[0] + '.o')))
    jobs.append((os.path.join(MOD, 'ffx_uw.c'), os.path.join(OBJ, 'module_ffx_uw.o')))   # the ultrawide patch set of the RevoFX FFX module (uwshim\\revofx.h)
    newest_h = max(os.path.getmtime(os.path.join(r, f)) for d in (MOD, HERE) for r, _, fs in os.walk(d) for f in fs if f.endswith('.h'))
    todo = [(s, o) for s, o in jobs if rebuild or not os.path.exists(o) or os.path.getmtime(o) < max(os.path.getmtime(s), newest_h)]
    def one(j):
        s, o = j
        if s.endswith('.c'): zig('cc', *CFLAGS, '-DF60_EXE', '-I' + os.path.join(HERE, 'uwshim'), '-c', s, '-o', o)
        else: zig('c++', *FLAGS, '-c', s, '-o', o)
    with cf.ThreadPoolExecutor(os.cpu_count() or 4) as ex: list(ex.map(one, todo))
    print('compiled %d of %d files' % (len(todo), len(jobs)))
    return [o for _, o in jobs]

def main():
    objs = compile_all('--rebuild' in sys.argv)
    img = os.path.join(OBJ, 'f60.img')                     # one relocatable PE image; the patcher relocates it per game
    zig('c++', '-target', 'x86-windows-gnu', '-shared', '-o', img, *objs, '-luser32', '-lwinmm', '-lpsapi', '-ld3d11', '-s')
    out = os.path.join(HERE, 'bin'); os.makedirs(out, exist_ok=True)
    # the optional DLL build: the same patch code with the DLL host (-DF60_DLL) and the dbghelp forwarding layer (gen_dbghelp.py)
    dll_dir = os.path.join(HERE, 'dll')
    dll_objs = [o for o in objs if os.path.basename(o) != 'f60_host.o']
    for s, o, fl in ((os.path.join(HERE, 'f60_host.cpp'), os.path.join(OBJ, 'dll_f60_host.o'), FLAGS + ['-DF60_DLL']),
                     (os.path.join(dll_dir, 'f60_dbghelp.cpp'), os.path.join(OBJ, 'dll_f60_dbghelp.o'), FLAGS),
                     (os.path.join(dll_dir, 'dbghelp_fwd.S'), os.path.join(OBJ, 'dll_dbghelp_fwd.o'), CFLAGS)):
        zig('c++' if s.endswith('.cpp') else 'cc', *fl, '-c', s, '-o', o); dll_objs.append(o)
    dll = os.path.join(out, 'dbghelp.dll')
    zig('c++', '-target', 'x86-windows-gnu', '-shared', '-o', dll, *dll_objs, os.path.join(dll_dir, 'dbghelp.def'), '-luser32', '-lwinmm', '-lpsapi', '-ld3d11', '-s')
    print('%s (%d KB)' % (dll, os.path.getsize(dll) // 1024))
    csc = os.path.join(os.environ['WINDIR'], 'Microsoft.NET', 'Framework', 'v4.0.30319', 'csc.exe')
    exe = os.path.join(out, 'FFX_enh_Patcher.exe')
    r = subprocess.run([csc, '/nologo', '/target:exe', '/platform:x86', '/optimize+', '/warn:4', '/define:PATCHER_EXE', '/out:' + exe,
                        '/resource:' + img + ',f60.img', '/resource:' + os.path.join(HERE, 'ffx_enh.ini') + ',ffx_enh.ini',
                        os.path.join(HERE, 'Patcher.cs')], capture_output=True, text=True)
    if r.returncode: sys.exit('csc failed:\n' + r.stdout + r.stderr)
    print('%s (%d KB)' % (exe, os.path.getsize(exe) // 1024))

if __name__ == '__main__': main()
