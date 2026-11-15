# Builds a VBF archive from a base archive plus a folder of replacement / additional files.
# Layout as written by Square Enix (checked against metamenu.vbf):
#   0   "SRYK", u32 header size, u64 file count
#   16  file count x 16 bytes: MD5 of each file name (entry order)
#       file count x 32 bytes: u32 first block, u32 first block (repeated), u64 size, u64 offset in the archive,
#                              u64 offset of the name in the name table
#       u32 name table size (this field included), names (zero terminated, entry order)
#       one u16 per 64 KB block of every file: its stored size; 0 = 65536; equal to the real size = stored, else zlib
#   header size: the file data, in entry order, without gaps
#   end: 16 bytes MD5 of the header (bytes 0 .. header size)
# Entries keep the base archive's order (names in lower case, '/' separators); added files follow, sorted.
#
#   python -I vbfbuild.py <base.vbf> <override dir> <out.vbf> [--prefix metamenu/]
# A file <override dir>/<path> replaces the entry <prefix><path> (case-insensitive) or is added. The output file must
# not exist yet. Base '-': no base archive, the archive holds only the folder's files (sorted by name), e.g. the
# launcher's metamenu_1440.vbf / metamenu_2160.vbf / metamenu_mod.vbf, which are read before metamenu.vbf.
import hashlib, os, struct, sys, zlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vbfextract import Vbf

def blocks_of(data):
    sizes, parts = [], []
    for k in range(0, len(data), 0x10000):
        raw = data[k:k + 0x10000]
        z = zlib.compress(raw, 9)
        if len(z) < len(raw):
            sizes.append(len(z)); parts.append(z)
        else:
            sizes.append(len(raw) & 0xFFFF); parts.append(raw)     # stored; a full 64 KB block is written as 0
    return sizes, parts

def main():
    a = sys.argv[1:]
    prefix = ''
    if '--prefix' in a:
        i = a.index('--prefix'); prefix = a[i + 1].lower(); del a[i:i + 2]
    if len(a) != 3:
        print(__doc__ or 'usage: vbfbuild.py <base.vbf> <override dir> <out.vbf> [--prefix p]'); return 1
    base, over, out = a
    if os.path.exists(out):
        print('output exists, not overwritten: ' + out); return 1
    v = Vbf(base) if base != '-' else None
    files = {}                                   # name -> (source description, loader)
    for i, e in enumerate(v.entries if v else []):
        files[e[0].lower()] = ('base', i)
    replaced = added = 0
    for dp, dn, fn in os.walk(over):
        for f in fn:
            p = os.path.join(dp, f)
            name = (prefix + os.path.relpath(p, over).replace(os.sep, '/')).lower()
            if name in files: replaced += 1
            else: added += 1
            files[name] = ('file', p)
    base_names = [e[0].lower() for e in v.entries] if v else []
    names = base_names + sorted(set(files) - set(base_names))
    n = len(names)
    name_table = b''.join(nm.encode('ascii') + b'\0' for nm in names)
    nblocks = sum((size_of(files[nm], v) + 0xFFFF) >> 16 for nm in names)
    hsize = 16 + n * 16 + n * 32 + 4 + len(name_table) + nblocks * 2
    tmp = out + '.part'
    md5s, recs, block_sizes = [], [], []
    noff = 0; off = hsize; blk = 0
    with open(tmp, 'wb') as o:
        o.write(b'\0' * hsize)
        for nm in names:
            src = files[nm]
            data = v.read(src[1]) if src[0] == 'base' else open(src[1], 'rb').read()
            sizes, parts = blocks_of(data)
            for part in parts: o.write(part)
            md5s.append(hashlib.md5(nm.encode('ascii')).digest())
            recs.append(struct.pack('<IIQQQ', blk, blk, len(data), off, noff))
            block_sizes += sizes
            off += sum(len(p) for p in parts); blk += len(sizes); noff += len(nm) + 1
        header = (b'SRYK' + struct.pack('<IQ', hsize, n) + b''.join(md5s) + b''.join(recs)
                  + struct.pack('<I', len(name_table) + 4) + name_table + struct.pack('<%dH' % len(block_sizes), *block_sizes))
        assert len(header) == hsize
        o.write(hashlib.md5(header).digest())
        o.seek(0); o.write(header)
    os.replace(tmp, out)
    print('%s: %d files (%d replaced, %d added), %d bytes' % (out, n, replaced, added, os.path.getsize(out)))
    return 0

def size_of(src, v):
    return v.entries[src[1]][1] if src[0] == 'base' else os.path.getsize(src[1])

if __name__ == '__main__':
    sys.exit(main())
