import base64,copy,unittest
from verify import FONTS,TEXT,expected_keys,validate_metadata
def sample():
    settings=[{'action':x,'success':True,'value':v,'error':0} for x,v in zip((0x4a,0x200a,0x200c,0x2012),(1,2,1200,1))]
    captures=[]
    for i,(font,route,slot,bg) in enumerate(sorted(expected_keys())):
        captures.append(dict(font=font,route=route,slot=slot,background=bg,foreground=-16777216,character=None if slot<0 else ord(TEXT[slot]),raw=f'{i}.rgba',png=f'{i}.png'))
    fonts=[dict(Name=n,sourceSha256=h,Metadata=dict(selectedLogFontBase64=base64.b64encode(bytes(92)).decode(),requestedQuality=0,fontSha256=h,glyphIndices=list(range(32)))) for n,h in FONTS.items()]
    return dict(schema='original-gdi-grid-reference-v1',qualified=False,inputEvents=0,desktopWindows=0,width=384,height=64,dpi=96,text=TEXT,elapsedSeconds=1,before=settings,after=copy.deepcopy(settings),fonts=fonts,captures=captures)
class Guards(unittest.TestCase):
    def test_complete_original_inventory(self):validate_metadata(sample())
    def reject(self,change):
        data=sample();change(data)
        with self.assertRaises((AssertionError,ValueError)):validate_metadata(data)
    def test_duplicate_and_missing_slot(self):self.reject(lambda d:d['captures'].__setitem__(0,d['captures'][1]))
    def test_changed_system_word(self):self.reject(lambda d:d['after'][2].update(value=1400))
    def test_deadline(self):self.reject(lambda d:d.update(elapsedSeconds=45))
    def test_nonfinite_deadline(self):self.reject(lambda d:d.update(elapsedSeconds=float('nan')))
    def test_font_substitution(self):self.reject(lambda d:d['fonts'][0].update(sourceSha256='0'*64))
    def test_quality_substitution(self):self.reject(lambda d:d['fonts'][0]['Metadata'].update(requestedQuality=5))
    def test_path_escape(self):self.reject(lambda d:d['captures'][0].update(raw='../outside.rgba'))
    def test_no_window_input(self):self.reject(lambda d:d.update(inputEvents=1))
    def test_wrong_space_identity(self):self.reject(lambda d:next(c for c in d['captures'] if c['slot']==4).update(character=65))
if __name__=='__main__':unittest.main()
