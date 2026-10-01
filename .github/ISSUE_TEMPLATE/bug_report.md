---
name: Bug report
about: Something doesn't behave the way the README says it should
title: ''
labels: bug
assignees: ''
---

<!--
Thanks for the report. Please search existing issues first.
Never paste API keys, tokens, or other secrets. Redact them before submitting.
-->

## HarnessGateway version

Run `harnessgateway --version` and paste the output (for example `0.1.0`).

```
harnessgateway --version
```

The version deliberately stays out of the archive file names, so `--version`
is the only reliable way to identify your build.

## Operating system

OS and version, e.g. `Windows 11 24H2`, `Ubuntu 24.04`, `macOS 26`.

## Exit code

The numeric exit code the command returned (see the README's Exit codes
table). On bash/zsh check `$?`, on PowerShell `$LASTEXITCODE`. If the command
exited 0, say so.

## Harness

Which harness you were launching, using its token (`opencode`, `kilo`,
`copilot`, `pi`, `codex`, `claude-code`; see the README's Harnesses table),
and whether it is on your `PATH` (`<binary> --version`).

## Backend shape

Report the protocol (`openai-chat`, `openai-responses`, or `anthropic`) and
what kind of endpoint it is (vendor API, self-hosted proxy, gateway, etc.).
Redact private hostnames or URLs as needed, and never include the API key
itself.

## What happened

The actual behavior, including any error output.

## What you expected

## Steps to reproduce

1.
2.
3.
