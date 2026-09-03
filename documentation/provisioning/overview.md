# Provisioning Overview

Provisioning imports domain translation rules, manual proxy hosts, automatic templates, container default sets, and certificates on startup from one JSON source.
It is intended for bootstrapping a node with predictable proxying, certificate, and container-default behavior.

## Configure source

Set one of:

- `Provisioning:FilePath`
- `Provisioning:Url`

Do not set both.

## Behavior

- runs once after app starts listening
- does not overwrite existing IDs
- logs failures without stopping startup
- supports template expansion from `TemplateValues` (for example `{server.name}`) in string values
- `domainTranslations[]` is for suffix-based domain translation rules
- `proxyHosts[]` is for manual proxy hosts
- `automaticTemplates[]` is for `automaticContainer` proxy host templates
- default sets are applied when a container carries `quickproxy.defaults=<id>`

## Main sections

- `domainTranslations[]`
- `proxyHosts[]`
- `automaticTemplates[]`
- `containerDefaultSets[]`
- `certificates[]`

## What provisioning does not do

- it does not overwrite an existing domain translation, proxy host, proxy host template, default set, or certificate with the same ID
- it does not force container defaults over existing label/env/mount/network-alias values

## Typical use

Use provisioning when you want to:

- bootstrap a set of domain translation rules for network-only backend domains
- bootstrap a set of manual proxy hosts for a new node
- bootstrap automatic proxy host templates from container labels
- ship reusable container default sets for common app types
- preload certificates or issuer definitions on first start


