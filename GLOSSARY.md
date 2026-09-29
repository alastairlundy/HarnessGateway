# Glossary

## Backend

An API endpoint a harness is pointed at, identified by a base URL and a protocol: OpenAI Chat Completions, OpenAI Responses, or Anthropic-compatible.

## Configuration action

A step the launcher performs to make a harness use a chosen backend: passing CLI flags or writing a harness config file. Harness environment variables are not configuration actions — they ride the spawned process plan and never mutate the launcher's own environment.

## Default endpoint

The user-saved backend that `launch` uses when no per-launch endpoint override is given.

## Adapter

The per-harness component that knows how to detect, configure, and start one harness.

## Harness

An agent tool the launcher can configure and start, which may be a CLI or a GUI app, such as OpenCode, Kilo, GitHub Copilot CLI, or Pi.

## Model catalog

The set of models a Backend declares as available.
