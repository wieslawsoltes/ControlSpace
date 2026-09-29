# Build, publication and release

## Initial delivery status

The requested repository is `wieslawsoltes/ControlSpace`. It was empty when inspected. The connected GitHub surface in this session exposed repository reads but not write operations. The local shell has Git but cannot resolve/reach GitHub or obtain a .NET SDK. Consequently, the delivered commit exists only in the local Git bundle; no remote commit, pull request, release or Pages deployment is claimed.

## Publish the supplied Git bundle

In a networked environment with authenticated Git access:

```sh
git clone ControlSpace.bundle ControlSpace
cd ControlSpace
git remote set-url origin https://github.com/wieslawsoltes/ControlSpace.git
git push -u origin main
```

This assumes the remote remains empty. Fetch and review first if other work has subsequently appeared; do not force-push over it.

Select **Settings → Pages → Build and deployment → Source → GitHub Actions** where required. The workflow also requests Pages enablement through `configure-pages`; repository policy may require a maintainer to configure the Pages environment first.

A push to `main` starts the build/test and Uno browser workflows. The Pages deploy job is gated on C# portable tests, a real Uno publish, package creation and a genuine Uno startup smoke check. The deployment should target `https://wieslawsoltes.github.io/ControlSpace/` after the workflow succeeds. That is a requested target, **not a verified live URL in the initial delivery**.

`build-info.json` records the exact commit and identifies the primary runtime as Uno. The post-deployment step checks that public metadata matches the current commit. The interaction prototype is a secondary `/prototype/` route; documentation is under `/docs/`.

## Local browser staging

The base path is significant. A build configured with `/ControlSpace/` must be served under that prefix:

```sh
dotnet workload install wasm-tools
dotnet publish src/ControlSpace.App -c Release -f net10.0-browserwasm \
  -p:ControlSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ControlSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
mkdir -p artifacts/serve
ln -s "$PWD/artifacts/site" artifacts/serve/ControlSpace
python3 -m http.server 4173 --directory artifacts/serve
```

On Windows, use a directory junction or copy `artifacts/site` to `artifacts/serve/ControlSpace` instead of the POSIX symlink command. The staging tool rejects missing Uno bootstrap or WASM binaries and never fills the root with the prototype as a fallback.

## Releases

Only tag after the build matrix and browser checks have passed:

```sh
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64; `.zip` on Windows, `.tar.gz` elsewhere), a browser archive, eight library `.nupkg`/`.snupkg` packages, an exact source ZIP and SHA-256 checksums. Versions with a pre-release suffix (for example `v0.2.0-preview.1`) are marked prerelease. After the GitHub Release is created, the `nuget` job publishes the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): it runs in the protected `nuget` environment, exchanges a GitHub OIDC token for a short-lived API key through `NuGet/login`, and needs only the `NUGET_USER` variable (the nuget.org profile name) — no API key is stored. Manual `workflow_dispatch` runs take a `version` input and are dry runs: they build, test and upload every asset as workflow artifacts but create no release and publish nothing. The workflow does not sign/notarize binaries, create installers, or certify runtime compatibility. macOS signing/notarization and Windows signing remain release-engineering work.

## Permissions

Normal build jobs need `contents: read`. Pages deployment uses `pages: write` and `id-token: write`. Only the release job uses `contents: write`, and only the tag-only `nuget` job receives `id-token: write` for Trusted Publishing. No user token is stored in source; the release uses the workflow's ephemeral `GITHUB_TOKEN`. Pull-request workflows do not deploy. All workflows in the source are active definitions, not disabled placeholders, but their remote operation remains unverified until a real run completes.
