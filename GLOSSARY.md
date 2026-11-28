# Glossary

## Configuration action

A step the launcher performs to make a harness use a chosen provider: passing CLI flags or writing a harness config file. Harness environment variables are not configuration actions — they ride the spawned process plan and never mutate the launcher's own environment.

## Configuration surface

A mechanism a harness can be handed launcher configuration through: environment variables, command-line flags, or configuration files. The launcher only uses the ones a harness declares, and picks no surface of its own.

## Default endpoint

The user-saved provider that `launch` uses when no per-launch endpoint override is given.

## Adapter

The per-harness component that knows how to detect, configure, and start one harness.

## Harness

An agent tool the launcher can configure and start, which may be a CLI or a GUI app, such as OpenCode, Kilo, GitHub Copilot CLI, or Pi.

## Launch flow

The ordered pipeline the launcher runs to resolve a provider and keys, write or restore harness configuration, and start the harness, behind one deep module's interface.

## Launch outcome

The closed set of terminal results a launch flow can produce: a refusal (user-rectifiable, no harness run), or a completed run carrying the harness's exit status when the launcher waited on the harness and a handoff fact when it returned without waiting.

## Restore guarantee

The invariant that every harness configuration change a launch makes is undone on every exit path, except where the user deliberately persisted it.

## Launch request

The declarative decisions a caller submits to a launch flow: which Harness, which Provider, which model, how to persist.

## Model catalog

The set of models a Provider declares as available.

## Provider

An API identity that owns a default endpoint, one API key reference, and a set of models, where each model may use its own wire protocol and endpoint override.

## Model entry

One named model in a provider's catalog. An entry may override the provider's default wire protocol, its endpoint, or both; absent overrides inherit the provider defaults.

## Update check

An on-demand CLI verb that queries GitHub Releases for a newer HarnessGateway version and reports the result without downloading, installing, or applying anything.

## Environment Variable Scrub

Removal of credential-bearing environment variables from the spawned harness process environment at the caller's request. The launcher's own environment is never scrubbed, and absent the request the child inherits everything.

## Harness-side provider

A provider identity a harness uses in its own model catalog to resolve model references. Distinct from a Provider, which is a saved launcher entry.
