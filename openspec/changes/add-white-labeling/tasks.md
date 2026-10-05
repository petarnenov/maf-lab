# Tasks

No decision-engine call is added or changed, so the Jev review checklist does not apply. Tone fragments change the
system prompt, so the evals run (task 3.2).

Depends on `rename-firm-to-tenant` (`TENANT_ADMIN`), `introduce-plugins`, `adopt-company-idp`,
`enable-plugins-per-tenant` and `data-lifecycle` (its inventory already lists the brand row).

## 1. Core

- [ ] 1.1 Installation brand from `Branding:*` and a mounted asset directory. `GET /api/branding` and the asset route
      answer anonymously. Neutral defaults. "maf-lab" is removed from the shell's text; storage keys are kept. Verify
      the scenarios "No branding plugin" and "Before sign-in".
- [ ] 1.2 The shell writes the brand's values to the existing tokens as `light-dark()` on `:root`. Verify with Vitest
      that a brand recolours the shell in Light, Dark and System modes.
- [ ] 1.3 The `IBrandProvider` port, contributed through `IContributesBrandProvider`. the api refuses to start with two
      contributors. The core checks `IPluginAccess.For(principal)` before every call, in `GET /api/branding`
      and in prompt assembly (the tone). Verify the scenarios "Two brand providers" and "Installed but disabled for the
      tenant".

## 2. The `branding` plugin

- [ ] 2.1 Brand rows and asset blobs through `IContributesModel` and `IContributesDataLifecycle`. Tenant assets are
      served only to the tenant's principals. Verify the scenario "A tenant with its own brand", and that another
      tenant's asset URL answers 404.
- [ ] 2.2 Validation and hardening:
  - WCAG 2.2 AA on the enumerated token pairs, in both themes;
  - PNG or WebP by content, with no animated WebP and within the caps;
  - SkiaSharp re-encode (`SkiaSharp` + `SkiaSharp.NativeAssets.Linux.NoDependencies` pinned in
    `Directory.Packages.props`), with a test in the api container that decodes and re-encodes a PNG and a WebP;
  - the `nosniff`, `inline` and immutable hash-URL headers;
  - the closed OFL woff2 font list;
  - name and greeting lengths;
  - the closed tone set.

  Verify with a test per rule, plus a polyglot PNG that comes back clean.
- [ ] 2.3 The `tenant-admin` section with a live preview in both themes. Saves are audited.

## 3. Assistant

- [ ] 3.1 The name is shown by the UI and never sent to the model. The greeting is the chat's empty state, never a
      message. Verify that a run's request carries neither.
- [ ] 3.2 Tone fragments through `IBrandProvider`. `make eval SUITE=generation`, `SUITE=selection` and the injection
      suite run for `formal` and `friendly` (`neutral` is the baseline) and hold their baselines. `tone` is in the prompt trace event and the eval settings.

## 4. Documents

- [ ] 4.1 `docs/plugins.md`, `docs/http-api.md`, the README (and the screenshot retake, DECISIONS §55), and DECISIONS
      §88 with the SkiaSharp pin.
