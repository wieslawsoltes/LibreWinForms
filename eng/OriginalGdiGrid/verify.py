"""Read-only fixed inventory verification, also used by CPU guard tests."""
import base64,hashlib,json,math,pathlib,struct
TEXT='Name Age 123 Hello Windows Forms'
FONTS={'Microsoft Sans Serif':'89b42a12ea0379133fb2f4a1d1bd53058fb61e2343c1d509452d5761acc85b7a','Segoe UI':'8134dbcd09e7b123c9a7f229d49cffbcb01352cc72ea5e1076b65d0dca9f73cd'}
BACKGROUNDS=(-1,-16777216,-13273675)
def expected_keys():
    keys=set()
    for font in FONTS:
        keys.add((font,'canonical-original-opaque',-1,-1))
        for background in BACKGROUNDS:
            keys.add((font,'canonical-transparent',-1,background))
            keys.add((font,'gdi-realized-text',-1,background))
            for slot in range(len(TEXT)):keys.add((font,'gdi-glyph-index',slot,background))
    return keys
def validate_metadata(data):
    assert data['schema']=='original-gdi-grid-reference-v1' and data['qualified'] is False
    assert data['inputEvents']==data['desktopWindows']==0
    assert (data['width'],data['height'],data['dpi'],data['text'])==(384,64,96,TEXT)
    assert isinstance(data['elapsedSeconds'],(int,float)) and math.isfinite(data['elapsedSeconds']) and 0<=data['elapsedSeconds']<45
    assert data['before']==data['after']
    assert [x['action'] for x in data['before']]==[0x004a,0x200a,0x200c,0x2012]
    assert len(data['fonts'])==2
    assert {x['Name']:x['sourceSha256'] for x in data['fonts']}==FONTS
    for font in data['fonts']:
        native=font['Metadata'];logfont=base64.b64decode(native['selectedLogFontBase64'],validate=True)
        assert len(logfont)==92 and logfont[26]==0 and native['requestedQuality']==0
        assert native['fontSha256']==font['sourceSha256'] and len(native['glyphIndices'])==len(TEXT)
    captures=data['captures'];assert len(captures)==206
    assert {(x['font'],x['route'],x['slot'],x['background']) for x in captures}==expected_keys()
    assert len({x['raw'] for x in captures})==len({x['png'] for x in captures})==206
    for c in captures:
        assert c['foreground']==-16777216
        assert c['character']==(None if c['slot']==-1 else ord(TEXT[c['slot']]))
        assert pathlib.PurePath(c['raw']).name==c['raw'] and pathlib.PurePath(c['png']).name==c['png']
        assert c['raw'].endswith('.rgba') and c['png'].endswith('.png')
def validate(root):
    root=pathlib.Path(root);data=json.loads((root/'reference.json').read_text());validate_metadata(data)
    total=png_bytes=0
    for c in data['captures']:
        raw=(root/c['raw']).read_bytes();png=(root/c['png']).read_bytes()
        assert len(raw)==384*64*4 and hashlib.sha256(raw).hexdigest()==c['rgbaSha256']
        assert png[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',png[16:24])==(384,64)
        assert hashlib.sha256(png).hexdigest()==c['pngSha256']
        total+=len(raw)+len(png);png_bytes+=len(png)
    assert total==data['evidenceBytes'] and total<=48*1024*1024 and png_bytes<=16*1024*1024
    return {'captures':206,'rawBytes':206*384*64*4,'pngBytes':png_bytes,'qualification':False}
