# Deployment

The checked-in `WindowsSmoke.pubxml` produces a framework-dependent Windows build for verification. Review signing, versioning, distribution, rollback, and organizational security requirements before a real release. This repository does not create credentials or deploy automatically.

## Release preparation

Run the complete release gate, inspect the publish output, and obtain the required signing and distribution approvals outside this repository.

## Rollback

Retain the prior signed artifact and its version metadata so distribution can be reverted without rebuilding or changing source history.
