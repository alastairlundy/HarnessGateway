# Glossary

## Backend

An API endpoint a harness is pointed at, identified by a base URL and a protocol: OpenAI Chat Completions, OpenAI Responses, or Anthropic-compatible.

## Configuration action

A step the launcher performs to make a harness use a chosen backend: passing CLI flags or writing a harness config file. Harness environment variables are not configuration actions — they ride the spawned process plan and never mutate the launcher's own environment.

## Configuration surface

A mechanism a harness can be handed launcher configuration through: environment variables, command-line flags, or configuration files. The launcher only uses the ones a harness declares, and picks no surface of its own.

## Default endpoint

The user-saved backend that `launch` uses when no per-launch endpoint override is given.

## Adapter

The per-harness component that knows how to detect, configure, and start one harness.

## Harness

An agent tool the launcher can configure and start, which may be a CLI or a GUI app, such as OpenCode, Kilo, GitHub Copilot CLI, or Pi.

## Launch flow

The ordered pipeline the launcher runs to resolve a backend and keys, write or restore harness configuration, and start the harness, behind one deep module's interface.

## Launch outcome

The closed set of terminal results a launch flow can produce: a refusal (user-rectifiable, no harness run), or a completed run carrying the harness's exit status when the launcher waited on the harness and a handoff fact when it returned without waiting.

## Restore guarantee

The invariant that every harness configuration change a launch makes is undone on every exit path, except where the user deliberately persisted it.

## Launch request

The declarative decisions a caller submits to a launch flow: which Harness, which Backend, which model, how to persist.

## Model catalog

The set of models a Backend declares as available.
