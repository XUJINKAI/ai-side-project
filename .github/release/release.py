"""Per-project rolling release. No third-party Python dependencies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
from datetime import datetime, timezone

TAG = 'latest-build'
PROJECTS = {
    'lansend': ['lansend-win-x64.exe', 'lansend-linux-amd64'],
    'force-break': ['force-break-win-x64.exe'],
}


def command(*args, data=None):
    result = subprocess.run(args, input=data, capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace'))
    return result.stdout


class GitHub:
    def __init__(self):
        self.repo = os.environ['GITHUB_REPOSITORY']
        self.root = f'repos/{self.repo}'

    def api(self, path, method='GET', value=None):
        args = ['gh', 'api', '--method', method, self.root + (f'/{path}' if path else '')]
        data = None
        if value is not None:
            args += ['--input', '-']
            data = json.dumps(value).encode()
        raw = command(*args, data=data)
        return json.loads(raw) if raw else None

    def release(self):
        # Listing distinguishes "no release" from authentication/network failures.
        for page in range(1, 100):
            releases = self.api(f'releases?per_page=100&page={page}')
            for release in releases:
                if release['tag_name'] == TAG:
                    return release
            if len(releases) < 100:
                return None
        raise RuntimeError('Release pagination limit exceeded')

    def assets(self, release):
        found = {}
        for page in range(1, 100):
            items = self.api(f'releases/{release["id"]}/assets?per_page=100&page={page}')
            found.update({a['name']: a for a in items})
            if len(items) < 100:
                return found
        raise RuntimeError('Asset pagination limit exceeded')

    def download(self, asset):
        return command('gh', 'api', f'{self.root}/releases/assets/{asset["id"]}',
                       '-H', 'Accept: application/octet-stream')

    def upload(self, path):
        for attempt in range(3):
            try:
                command('gh', 'release', 'upload', TAG, str(path), '--clobber', '--repo', self.repo)
                return
            except RuntimeError:
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)

    def delete(self, asset):
        self.api(f'releases/assets/{asset["id"]}', 'DELETE')


def fingerprint(project, ref='HEAD'):
    paths = [project, f'.github/workflows/{project}.yml',
             '.github/workflows/publish-build.yml', '.github/release']
    hashes = [command('git', 'rev-parse', f'{ref}:{p}').decode().strip() for p in paths]
    return hashlib.sha256('\n'.join(hashes).encode()).hexdigest()


def manifest_name(project):
    return f'{project}-build.json'


def needs_build(project, fingerprint_value, assets, manifest):
    return (manifest is None or manifest.get('build_inputs_sha256') != fingerprint_value
            or any(name not in assets for name in PROJECTS[project] + [manifest_name(project)]))


def plan(project):
    # PRs validate their changes but never publish, even from same-repository branches.
    if os.environ.get('GITHUB_EVENT_NAME') in ('pull_request', 'workflow_dispatch'):
        rebuild = True
    else:
        gh = GitHub()
        release = gh.release()
        assets = gh.assets(release) if release else {}
        item = assets.get(manifest_name(project))
        manifest = json.loads(gh.download(item)) if item else None
        rebuild = needs_build(project, fingerprint(project), assets, manifest)
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f'build={str(rebuild).lower()}\n')
    print(f'{project}: {"build" if rebuild else "already published; skip"}')


def make_info(project, folder):
    files = []
    for name in PROJECTS[project]:
        data = (folder / name).read_bytes()
        files.append({'name': name, 'os': 'windows' if name.endswith('.exe') else 'linux',
                      'architecture': 'x86_64', 'size_bytes': len(data),
                      'sha256': hashlib.sha256(data).hexdigest()})
    toolchain = command('go', 'version') if project == 'lansend' else command('dotnet', '--version')
    info = {
        'schema_version': 1, 'project': project,
        'repository': os.environ['GITHUB_REPOSITORY'],
        'source_commit': command('git', 'rev-parse', 'HEAD').decode().strip(),
        'source_ref': os.environ['GITHUB_REF'],
        'build_inputs_sha256': fingerprint(project),
        'built_at': datetime.now(timezone.utc).isoformat(),
        'workflow_run_url': f'https://github.com/{os.environ["GITHUB_REPOSITORY"]}/actions/runs/{os.environ["GITHUB_RUN_ID"]}',
        'toolchain': toolchain.decode().strip(),
        'runtime_requirement': 'none (CGO_ENABLED=0)' if project == 'lansend' else '.NET 10 Desktop Runtime x64 (not bundled)',
        'files': files,
    }
    (folder / manifest_name(project)).write_text(json.dumps(info, indent=2) + '\n', encoding='utf-8')


def validate_bundle(project, folder):
    expected = PROJECTS[project] + [manifest_name(project)]
    if sorted(p.name for p in folder.iterdir()) != sorted(expected):
        raise ValueError('Unexpected or missing distribution files')
    info = json.loads((folder / manifest_name(project)).read_text(encoding='utf-8'))
    if info.get('schema_version') != 1 or info.get('project') != project:
        raise ValueError('Invalid build metadata')
    if [f['name'] for f in info['files']] != PROJECTS[project]:
        raise ValueError('Metadata file list differs from the project allowlist')
    for file in info['files']:
        data = (folder / file['name']).read_bytes()
        if len(data) != file['size_bytes'] or hashlib.sha256(data).hexdigest() != file['sha256']:
            raise ValueError(f'Checksum mismatch: {file["name"]}')
    return info


def replace_project(gh, release, project, folder):
    """Back up only this project's assets; JSON is the last file committed.

    Best-effort rollback handles upload/API failures. GitHub does not provide atomic
    multi-asset updates; forced runner termination can interrupt rollback.
    """
    validate_bundle(project, folder)
    names = PROJECTS[project] + [manifest_name(project)]
    original = gh.assets(release)
    with tempfile.TemporaryDirectory() as temp:
        backup = Path(temp)
        for name in names:
            if name in original:
                (backup / name).write_bytes(gh.download(original[name]))
        try:
            for name in names:
                gh.upload(folder / name)
            uploaded = gh.assets(release)
            for name in names:
                actual = gh.download(uploaded[name])
                if actual != (folder / name).read_bytes():
                    raise RuntimeError(f'Uploaded bytes differ: {name}')
        except Exception as error:
            failures = []
            for name in names:
                try:
                    if name in original:
                        gh.upload(backup / name)
                    else:
                        current = gh.assets(release).get(name)
                        if current:
                            gh.delete(current)
                except Exception as rollback_error:
                    failures.append(f'{name}: {rollback_error}')
            if failures:
                raise RuntimeError('Upload failed; rollback incomplete: ' + '; '.join(failures)) from error
            raise


def remove_retired_projects(gh, release):
    """Remove build bundles whose project was removed from the repository catalog."""
    assets = gh.assets(release)
    protected = {name for project, names in PROJECTS.items() for name in names + [manifest_name(project)]}
    for name, asset in assets.items():
        if not name.endswith('-build.json') or name in protected:
            continue
        try:
            info = json.loads(gh.download(asset))
        except (ValueError, UnicodeError):
            continue
        if not isinstance(info, dict):
            continue
        project = info.get('project')
        if (not isinstance(project, str) or project in PROJECTS or
                name != manifest_name(project) or info.get('repository') != gh.repo or
                info.get('schema_version') != 1 or not isinstance(info.get('files'), list)):
            continue
        filenames = [item.get('name') for item in info['files'] if isinstance(item, dict)]
        if any(not isinstance(item, str) or not item.startswith(project + '-') or
               '/' in item or '\\' in item or item in protected for item in filenames):
            continue
        for filename in set(filenames):
            if filename in assets:
                gh.delete(assets[filename])
        gh.delete(asset)


def publish(project, folder):
    gh = GitHub()
    default = gh.api('')['default_branch']
    if os.environ.get('GITHUB_REF') != f'refs/heads/{default}':
        raise RuntimeError('Publishing is restricted to the default branch')
    info = validate_bundle(project, folder)
    if info['repository'] != gh.repo or info['source_commit'] != command('git', 'rev-parse', 'HEAD').decode().strip():
        raise ValueError('Bundle is not from this checked-out build')
    command('git', 'fetch', '--no-tags', 'origin', f'refs/heads/{default}')
    # A slow old build must not replace a newer build. Compare actual inputs, so
    # unrelated commits to another project do not invalidate this project's build.
    if info['build_inputs_sha256'] != fingerprint(project, 'FETCH_HEAD'):
        print(f'{project}: source inputs changed on {default}; skip stale publication')
        return
    release = gh.release()
    if release is None:
        release = gh.api('releases', 'POST', {
            'tag_name': TAG, 'target_commitish': command('git', 'rev-parse', 'FETCH_HEAD').decode().strip(),
            'name': 'Latest builds · 主分支最新构建', 'make_latest': 'true',
            'body': '各项目最近一次成功构建的集合；不同项目可以来自不同提交。源码提交、构建时间、运行时要求和 SHA-256 以各自的 build.json 为准。\n\n附件为原始可执行文件，不包含运行时或安装压缩包。Linux 下载后如无执行权限，运行 chmod +x lansend-linux-amd64。\n\n本 Release 为滚动更新；自动生成的 Source code 附件对应初始化标签，不代表所有二进制的源码版本。',
        })
    if release.get('immutable') or release.get('draft'):
        raise RuntimeError('latest-build must be a published, mutable release')
    assets = gh.assets(release)
    old = assets.get(manifest_name(project))
    previous = json.loads(gh.download(old)) if old else None
    if needs_build(project, info['build_inputs_sha256'], assets, previous) or os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch':
        replace_project(gh, release, project, folder)
        print(f'Published {project}: {release["html_url"]}')
    else:
        print(f'{project}: identical inputs already published; skip duplicate upload')
    remove_retired_projects(gh, release)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=['plan', 'info', 'validate', 'publish'])
    parser.add_argument('project', choices=PROJECTS)
    parser.add_argument('folder', nargs='?', type=Path)
    args = parser.parse_args()
    if args.operation == 'plan':
        plan(args.project)
    elif args.folder is None:
        parser.error('folder is required')
    elif args.operation == 'info':
        make_info(args.project, args.folder)
    elif args.operation == 'validate':
        validate_bundle(args.project, args.folder)
    else:
        publish(args.project, args.folder)


if __name__ == '__main__':
    main()
