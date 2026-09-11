# Privacy Policy — FluentFlyout Downstream

*Last updated: 2026-09-11*

This policy describes the supported downstream Windows x64 portable ZIP in
[`MERURUXD/FluentFlyout`](https://github.com/MERURUXD/FluentFlyout). It is not
the privacy policy for the official upstream FluentFlyout product, its website,
or Microsoft Store distribution. See the [upstream policy](https://github.com/unchihugo/FluentFlyout/blob/master/PRIVACY-POLICY.md)
for that product.

## 1. What the downstream application sends

The downstream policy is code-owned and is not re-enabled by imported settings:

- Upstream telemetry is disabled. The application does not send the upstream
  event payloads, UUID/session identifiers, or experiment information used by
  the official service.
- Upstream experiments are disabled. The retained experiment service returns
  without contacting the upstream API.
- The supported build may request metadata from the downstream GitHub Releases
  API when it has a stable `vMAJOR.MINOR.PATCH` identity. Development and rolling
  `dev` builds skip this request. The response is used only to compare a stable
  version and to show the fixed downstream release page; the updater never
  downloads, executes, installs, or replaces application files.
- Links opened by an explicit user action can visit GitHub, the upstream
  website, Weblate, or another external destination. Those destinations have
  their own privacy policies and may receive ordinary web-request metadata.

This means the supported product is not “completely offline”: a stable update
metadata check and user-selected external links are allowed network boundaries.
The application does not claim to control data collected by GitHub, Microsoft,
Cloudflare, the upstream website, or other sites.

## 2. Local settings and logs

The downstream product writes its settings to:

```text
%AppData%\FluentFlyoutDownstream\settings.xml
```

Logs are written to the same downstream directory. The product keeps a backup
while replacing settings. If the downstream settings and backup do not exist on
first run, an existing `%AppData%\FluentFlyout\settings.xml` or `.bak` may be
read once as migration input. The legacy files are not a write destination;
they are left unchanged. Migration creates a new downstream UUID and clears the
persisted Store identity so the two products do not share that identity.

Settings and logs can contain user-chosen configuration, media/app names, or
diagnostic details. Review and redact them before sharing an issue or support
request. Do not attach a complete settings file or log containing personal
information unless it has been sanitized.

## 3. Windows and distribution services

The supported downstream channel is a portable ZIP. It is not an official
Microsoft Store listing or signed MSIX distribution. Windows may still collect
diagnostic data according to the user's Windows privacy settings, and GitHub
may process repository, release, and API request metadata according to its own
policies.

The repository retains upstream-derived MSIX/Store code and workflows for
compatibility and future synchronization. Those paths are not the supported
downstream ZIP distribution and this policy does not claim that every
upstream-derived build configuration has identical network or Store behavior.

## 4. Security and contact

The application uses HTTPS for the downstream release metadata request. No
credentials, tokens, or Store purchase data are required for the supported ZIP
channel. Report a downstream issue through the repository's [issue chooser](https://github.com/MERURUXD/FluentFlyout/issues/new/choose)
after removing secrets and personal data from the report.

The downstream fork has not yet designated an independent privacy or Code of
Conduct contact. The upstream contact in the inherited Code of Conduct belongs
to the upstream project and should not be treated as a downstream commitment;
the maintainer must confirm a downstream contact before one is published.

## 5. Changes to this policy

Changes will be made in this repository and dated in the file. A change to the
upstream product's policy does not automatically change this downstream policy.
