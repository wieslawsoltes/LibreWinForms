import copy,json,pathlib,unittest
from verify import validate_metadata,RECIPES
def sample():
    original=json.loads((pathlib.Path(__file__).parent/'reference/original.json').read_text())
    selections=[dict(recipe=r,selectedLogFontBase64=original['selectedLogFontBase64'],exactOriginalLogFont=True,fontSha256='a'*64,exactHistoricalFont=False,fontBytes=960076,matchingWindowsFontsFile=dict(sha256='a'*64,exactSelectedBytes=True)) for r in RECIPES]
    return dict(original=original,selections=selections,qualified=False,completed=True,inputEvents=0,desktopWindows=0,imageCaptures=0,elapsedSeconds=1)
class Guards(unittest.TestCase):
    def test_metadata_mismatch_is_not_original_admission(self):self.assertFalse(validate_metadata(sample())['historicalFontBytesMatchBoth'])
    def test_exact_historical_identity_still_not_qualified(self):
        data=sample()
        for selection in data['selections']:
            selection['fontSha256']=data['original']['fontSha256']
            selection['fontBytes']=data['original']['fontBytes']
            selection['exactHistoricalFont']=True
            selection['matchingWindowsFontsFile']['sha256']=data['original']['fontSha256']
        result=validate_metadata(data)
        self.assertTrue(result['historicalFontBytesMatchBoth'])
        self.assertFalse(result['qualified'])
    def reject(self,change):
        data=sample();change(data)
        with self.assertRaises(AssertionError):validate_metadata(data)
    def test_false_baseline_claim(self):self.reject(lambda d:d['selections'][0].update(exactHistoricalFont=True))
    def test_false_file_claim(self):self.reject(lambda d:d['selections'][0]['matchingWindowsFontsFile'].update(sha256='b'*64))
    def test_missing_recipe(self):self.reject(lambda d:d['selections'].pop())
    def test_deadline(self):self.reject(lambda d:d.update(elapsedSeconds=15))
    def test_no_images(self):self.reject(lambda d:d.update(imageCaptures=1))
if __name__=='__main__':unittest.main()
