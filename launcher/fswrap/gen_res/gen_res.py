import json, sys
sp = sys.argv[1]
t = json.load(open(sp + '/gen_res_texts.json', encoding='utf-8'))
src = open(sp + '/LauncherRes.template.cs', encoding='ascii').read()
BS = chr(92)
def cs(s):
    out = []
    for ch in s:
        if ch == '"': out.append(BS + '"')
        elif ch == BS: out.append(BS + BS)
        elif ord(ch) > 126: out.append(BS + 'u%04x' % ord(ch))
        else: out.append(ch)
    return '"' + ''.join(out) + '"'
for k, v in t.items():
    assert len(v) == 8, k
    src = src.replace('{{' + k + '}}', '{ ' + ', '.join(cs(x) for x in v) + ' }')
assert '{{' not in src
src = src.replace('\r\n', '\n').replace('\n', '\r\n')
assert all(ord(c) < 128 for c in src)
open(sys.argv[2], 'w', newline='', encoding='ascii').write(src)
print('written', sys.argv[2], len(src))
