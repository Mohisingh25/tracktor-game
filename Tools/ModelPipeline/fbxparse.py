import struct, zlib, sys, json
def parse(path):
    data = open(path,'rb').read()
    ver = struct.unpack_from('<I', data, 23)[0]
    big = ver >= 7500
    def rd_node(off):
        if big:
            end, nprops, plen = struct.unpack_from('<QQQ', data, off); off += 24
        else:
            end, nprops, plen = struct.unpack_from('<III', data, off); off += 12
        nlen = data[off]; off += 1
        name = data[off:off+nlen].decode('latin1'); off += nlen
        if end == 0: return None, off
        props = []
        p = off
        for _ in range(nprops):
            t = chr(data[p]); p += 1
            if t == 'Y': props.append(struct.unpack_from('<h',data,p)[0]); p+=2
            elif t == 'C': props.append(data[p]); p+=1
            elif t == 'I': props.append(struct.unpack_from('<i',data,p)[0]); p+=4
            elif t == 'F': props.append(struct.unpack_from('<f',data,p)[0]); p+=4
            elif t == 'D': props.append(struct.unpack_from('<d',data,p)[0]); p+=8
            elif t == 'L': props.append(struct.unpack_from('<q',data,p)[0]); p+=8
            elif t in 'fdlib':
                ln, enc, clen = struct.unpack_from('<III',data,p); p+=12
                raw = data[p:p+clen]; p+=clen
                if enc: raw = zlib.decompress(raw)
                fmt = {'f':'f','d':'d','l':'q','i':'i','b':'b'}[t]
                props.append(list(struct.unpack('<%d%s'%(ln,fmt), raw)))
            elif t in 'SR':
                ln = struct.unpack_from('<I',data,p)[0]; p+=4
                b = data[p:p+ln]; p+=ln
                props.append(b.decode('latin1') if t=='S' else b)
        off = off + plen
        children = []
        while off < end:
            c, off2 = rd_node(off)
            if c is None: off = off2; break
            children.append(c); off = off2
        return {'name':name,'props':props,'children':children}, end
    off = 27; nodes=[]
    while off < len(data)-100:
        n, off = rd_node(off)
        if n is None: break
        nodes.append(n)
    return ver, nodes
