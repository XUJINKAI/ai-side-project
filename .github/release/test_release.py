import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import release


class FakeGitHub:
    def __init__(self, files=None, fail_name=None):
        self.files = dict(files or {})
        self.fail_name = fail_name
        self.failed = False
        self.operations = []

    def assets(self, _):
        return {name: {'name': name, 'id': name} for name in self.files}

    def download(self, asset):
        return self.files[asset['name']]

    def upload(self, path):
        self.operations.append(path.name)
        self.files.pop(path.name, None)  # Model gh --clobber deleting before upload.
        if path.name == self.fail_name and not self.failed:
            self.failed = True
            raise RuntimeError('simulated failed upload after old asset deletion')
        self.files[path.name] = path.read_bytes()

    def delete(self, asset):
        del self.files[asset['name']]


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)

    def bundle(self, project):
        files = []
        for name in release.PROJECTS[project]:
            data = ('new executable: ' + name).encode()
            (self.folder / name).write_bytes(data)
            files.append({'name': name, 'sha256': hashlib.sha256(data).hexdigest(), 'size_bytes': len(data)})
        info = {'schema_version': 1, 'project': project, 'build_inputs_sha256': 'inputs',
                'source_commit': 'commit', 'repository': 'owner/repo', 'files': files}
        (self.folder / release.manifest_name(project)).write_text(json.dumps(info))
        return info

    def test_bedtime_update_preserves_lansend_and_commits_json_last(self):
        self.bundle('bedtime-guard')
        old = {'lansend-win-x64.exe': b'keep windows', 'lansend-linux-amd64': b'keep linux',
               'lansend-build.json': b'keep metadata', 'bedtime-guard-win-x64.exe': b'old exe',
               'bedtime-guard-build.json': b'old metadata'}
        gh = FakeGitHub(old)
        release.replace_project(gh, {}, 'bedtime-guard', self.folder)
        for name in release.PROJECTS['lansend'] + ['lansend-build.json']:
            self.assertEqual(gh.files[name], old[name])
        self.assertEqual(gh.operations[-1], 'bedtime-guard-build.json')
        self.assertEqual(set(gh.files), set(old))

    def test_failed_second_lansend_upload_restores_entire_old_project(self):
        self.bundle('lansend')
        old = {name: b'old ' + name.encode() for name in release.PROJECTS['lansend'] + ['lansend-build.json']}
        old['bedtime-guard-win-x64.exe'] = b'other project'
        gh = FakeGitHub(old, fail_name='lansend-linux-amd64')
        with self.assertRaises(RuntimeError):
            release.replace_project(gh, {}, 'lansend', self.folder)
        self.assertEqual(gh.files, old)

    def test_failed_first_publication_removes_partial_new_assets(self):
        self.bundle('lansend')
        gh = FakeGitHub({'bedtime-guard-build.json': b'keep'}, fail_name='lansend-build.json')
        with self.assertRaises(RuntimeError):
            release.replace_project(gh, {}, 'lansend', self.folder)
        self.assertEqual(gh.files, {'bedtime-guard-build.json': b'keep'})

    def test_modified_binary_never_reaches_upload(self):
        self.bundle('bedtime-guard')
        (self.folder / 'bedtime-guard-win-x64.exe').write_bytes(b'tampered')
        gh = FakeGitHub()
        with self.assertRaises(ValueError):
            release.replace_project(gh, {}, 'bedtime-guard', self.folder)
        self.assertEqual(gh.operations, [])

    def test_unexpected_file_is_not_published(self):
        self.bundle('lansend')
        (self.folder / 'runtime.dll').write_bytes(b'not allowed')
        with self.assertRaises(ValueError):
            release.validate_bundle('lansend', self.folder)

    def test_missing_binary_rebuilds_even_when_metadata_matches(self):
        info = self.bundle('lansend')
        assets = {p.name: {} for p in self.folder.iterdir()}
        self.assertFalse(release.needs_build('lansend', 'inputs', assets, info))
        del assets['lansend-linux-amd64']
        self.assertTrue(release.needs_build('lansend', 'inputs', assets, info))

    def test_new_inputs_and_first_publication_build(self):
        info = self.bundle('lansend')
        assets = {p.name: {} for p in self.folder.iterdir()}
        self.assertTrue(release.needs_build('lansend', 'different', assets, info))
        self.assertTrue(release.needs_build('lansend', 'inputs', {}, None))

    def test_stale_build_cannot_touch_release(self):
        self.bundle('bedtime-guard')
        gh = FakeGitHub()
        gh.repo = 'owner/repo'
        gh.api = lambda _: {'default_branch': 'master'}
        with patch.object(release, 'GitHub', return_value=gh), patch.object(release, 'command', return_value=b'commit\n'), \
             patch.object(release, 'fingerprint', return_value='newer inputs'), \
             patch.dict(release.os.environ, {'GITHUB_REF': 'refs/heads/master'}):
            release.publish('bedtime-guard', self.folder)
        self.assertEqual(gh.operations, [])

    def test_nondefault_branch_cannot_publish(self):
        gh = FakeGitHub()
        gh.repo = 'owner/repo'
        gh.api = lambda _: {'default_branch': 'master'}
        with patch.object(release, 'GitHub', return_value=gh), \
             patch.dict(release.os.environ, {'GITHUB_REF': 'refs/heads/feature'}):
            with self.assertRaises(RuntimeError):
                release.publish('bedtime-guard', self.folder)
        self.assertEqual(gh.operations, [])


if __name__ == '__main__':
    unittest.main()
