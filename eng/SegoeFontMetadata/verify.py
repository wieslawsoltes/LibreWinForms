import base64,json,math,pathlib
RECIPES={'original-Font.ToHfont','CreateFontIndirectW-exact-original-LOGFONT'}
def validate_metadata(data):
    assert data['qualified'] is False and data['completed'] is True
    assert data['inputEvents']==data['desktopWindows']==data['imageCaptures']==0
    assert math.isfinite(data['elapsedSeconds']) and 0<=data['elapsedSeconds']<15
    original=data['original'];assert original['fontSha256']=='8134dbcd09e7b123c9a7f229d49cffbcb01352cc72ea5e1076b65d0dca9f73cd'
    assert len(data['selections'])==2 and {s['recipe'] for s in data['selections']}==RECIPES
    old=base64.b64decode(original['selectedLogFontBase64'],validate=True);assert len(old)==92
    for selection in data['selections']:
        actual=base64.b64decode(selection['selectedLogFontBase64'],validate=True);assert len(actual)==92
        assert selection['exactOriginalLogFont']==(actual==old)
        assert selection['exactHistoricalFont']==(selection['fontSha256']==original['fontSha256'])
        file=selection['matchingWindowsFontsFile']
        if 'sha256' in file:assert file['exactSelectedBytes']==(file['sha256']==selection['fontSha256'])
        assert 0<selection['fontBytes']<=32*1024*1024
    return {'metadataComplete':True,'qualified':False,'historicalFontBytesMatchBoth':all(s['exactHistoricalFont'] for s in data['selections'])}
def validate(root):return validate_metadata(json.loads((pathlib.Path(root)/'metadata.json').read_text()))
