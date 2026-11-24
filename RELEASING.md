# Releasing Arkki

A release is a tag. GitHub Actions does the rest: it builds and tests the tagged source on Linux with
every external validator, packs `Arkki.Pdf` and `Arkki.Pdf.Signing` with the tag's version, attests
their provenance, publishes them on [Releases](https://github.com/geezery/arkki/releases) and pushes
them to [nuget.org](https://www.nuget.org/packages/Arkki.Pdf).

## Before the first release: the nuget.org key

Pushing to nuget.org needs a key that only the owner of the nuget.org account can make. Once:

1. Sign in at <https://www.nuget.org>, open **API Keys** and create a key with the scope
   *Push new packages and package versions* and the glob pattern `Arkki.*`.
2. Save it as the repository secret `NUGET_API_KEY`:

   ```sh
   gh secret set NUGET_API_KEY --repo geezery/arkki
   ```

Without the secret the release workflow still builds, attests and publishes the GitHub release, and
ends with a warning that nothing was pushed to nuget.org. Set the secret and run the workflow again
for the same tag (see below) to push the packages.

A key expires after at most a year. When a release warns about a rejected key, make a new one and
set the secret again.

## Make a release

1. Write the release's section in `CHANGELOG.md` (`## [1.0.0] - 2026-12-01`) and its link at the bottom.
   The workflow refuses a tag whose version has no section.
2. Set the same version in `Directory.Build.props` (`<Version>`), so local builds agree with the tag.
3. If the release adds, changes or removes a feature or a limit, fix `README.md`, `docs/wiki/`,
   `site/index.html` and `site/llms.txt`. The homepage names the current version in its hero badge
   and in `llms.txt`.
4. Commit, push, wait for CI to pass on `main`, then tag:

   ```sh
   git pull
   git tag v1.0.0
   git push origin v1.0.0
   ```

Then watch **Actions → Release**. It takes a few minutes. A version with a suffix (`v1.0.0-rc.1`)
becomes a pre-release on GitHub and a prerelease package on nuget.org.

## Numbering

Arkki uses [semantic versioning](https://semver.org/):

- `v1.0.1`: fixes only
- `v1.1.0`: new features, nothing existing breaks
- `v2.0.0`: a public API changed or went away
- `v1.1.0-rc.1`: a release candidate of 1.1.0

## If something goes wrong

- **The workflow fails in Version:** the tag is not a version, or `CHANGELOG.md` has no section for it.
  Fix the changelog on `main`, delete the tag (`git push origin :v1.0.0`, `git tag -d v1.0.0`) and tag again.
- **Build it again for the same tag:** Actions → Release → *Run workflow*, and enter the tag. The files
  on the existing GitHub release are replaced, and packages already on nuget.org are skipped.
- **A broken release went out:** nuget.org never lets a version be replaced. Unlist it on nuget.org
  (*Manage package → Listing*), fix the problem and tag the next version.

## Verify a release

```sh
gh attestation verify Arkki.Pdf.1.0.0.nupkg --repo geezery/arkki
sha256sum -c Arkki.Pdf.1.0.0.nupkg.sha256
```
