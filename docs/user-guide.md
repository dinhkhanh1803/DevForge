# DevForge Studio User Guide

## Install

DevForge Studio is distributed for the first release as a self-contained `win-x64` directory. Copy the complete audited directory to a user-writable local folder and start `DevForge.Desktop.exe`. Keep the `blueprints`, `docs`, DLL, JSON, and native runtime files beside the EXE. There is no installer, updater, browser shell, cloud backend, or Administrator requirement.

The supported release certification still requires Windows 11. The current local package evidence was produced on Windows 10 build 19045 and is not final Windows 11 certification.

## First run

On first run, DevForge creates its guarded local SQLite metadata store under `%LOCALAPPDATA%\DevForge`, loads the three built-in checksummed blueprints, applies the selected theme, and opens Settings until onboarding is complete. Environment Doctor reports the detected local tools without installing or changing them. If migration or recovery cannot complete safely, the shell opens in read-only safe mode and disables mutating actions.

## Create Project

1. Open **Create Project** and choose one built-in blueprint: C# WPF tool, React/Vite/TypeScript, or Python CLI.
2. Enter the project name, absent target directory, blueprint inputs, team profile, and optional reviewed Git intent.
3. Choose **Review plan**. Review the exact inputs, steps, expected files, warnings, Git branches, and private-by-default GitHub intent.
4. Choose **Create project** only after the plan is correct. Changing an input invalidates the reviewed plan and requires a new review.
5. Follow Execution Center. Cancel, Resume, Retry, and Cleanup are enabled only when the durable checkpoint proves the action safe.

DevForge generates into an owned staging area, runs the blueprint's closed validators, scans for secrets, writes integrity evidence, then finalizes without overwriting an existing target.

## Recovery and Run History

Startup Recovery normalizes interrupted work before normal navigation. Run History shows the durable status and only the actions allowed for that checkpoint. Use **Resume** for an interrupted resumable step, **Retry** for a retryable failure, and **Cleanup** only for marker-owned staging. Never delete or rename recovery markers manually.

A locally completed project remains usable even if optional publication fails. Publication failure becomes `PublishPending`; retry continues from durable Git/GitHub evidence and does not regenerate the project or duplicate the initial commit.

## Git and GitHub

Git initialization is optional and runs only after quality gates. DevForge creates the fixed bootstrap commit and supports `main` or `main + develop`. GitHub publication is separately reviewed, personal-account only, and private by default. Authentication remains delegated to the installed `gh` client; DevForge never asks for or records a token. It never force-pushes, deletes a remote, or adopts an unrelated repository.

## Support bundle

From Execution Center or Run History, choose **Support bundle** for an authoritative saved run. DevForge creates a privacy-safe ZIP in owned local diagnostics storage. **Copy receipt** copies only the relative bundle path, SHA-256, and byte length. The bundle does not include customer source, `.env` content, a database, credentials, or raw environment properties. See [Privacy and support bundles](privacy-and-support-bundles.md).

## Safe mode

Safe mode is read-only. Inspect settings, cached environment state, run history, and verified support evidence, but do not expect creation, cleanup, settings saves, or publication to be enabled. Preserve the local-data directory and use the remediation in [Troubleshooting](troubleshooting.md).
