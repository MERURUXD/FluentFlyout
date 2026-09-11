FluentFlyout Downstream portable ZIP

This archive is a personal downstream build of FluentFlyout from
https://github.com/MERURUXD/FluentFlyout. It is not the official FluentFlyout
product, a Microsoft Store package, or an MSIX installer.

Run FluentFlyoutDownstream.exe after extracting the ZIP. The archive is
self-contained for Windows x64 and does not install or replace another
FluentFlyout installation automatically.

RELEASE-INFO.txt records the build channel, manifest-derived version, full
source commit, source state, and source note. A clean release-capable archive
records that its immutable source URL is the exact source for the archive. A
development validation archive made from a dirty checkout is explicitly marked
dirty; its URL identifies only the base commit and it is not claimed to be
reproducible from that commit. The rolling dev archive is a prerelease and is
not a stable update.

The in-app updater reads stable release metadata only and opens the downstream
release page. It never downloads, executes, installs, or replaces an update.

The root LICENSE contains the project's GPL-3.0-or-later license and upstream
attribution. THIRD-PARTY-NOTICES.txt and the licenses/ directory contain the
resolved dependency and .NET runtime license/notice information used for this
build.
