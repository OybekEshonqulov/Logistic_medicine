import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('cleanup', Path(__file__).with_name('cleanup.py'))
cleanup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cleanup)


class CleanupTests(unittest.TestCase):
    def test_only_old_project_tags_are_removed(self):
        images = [{'Created': str(i).zfill(2), 'Id': str(i), 'RepoTags': ['pharmatrack-api:release-' + str(i).zfill(40)]} for i in range(9)]
        images.append({'Created': '00', 'Id': 'other', 'RepoTags': ['playcontrol-api:release-' + 'a' * 40, 'pharmatrack-api:initial']})
        current = images[0]['RepoTags'][0]
        actual = cleanup.candidates(images, {'1'}, current)
        self.assertEqual(actual, [images[3]['RepoTags'][0], images[2]['RepoTags'][0]])


if __name__ == '__main__':
    unittest.main()
