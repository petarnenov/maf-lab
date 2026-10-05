# Proposal

## Why

A universal enterprise assistant is sold to organisations that want it to look and speak as their own. Today the name
"maf-lab" is hard-coded in the shell (`web/index.html`, `components/Layout.tsx`), and the theme is one fixed set of
`light-dark()` tokens (`web/src/index.css:2-33`).

The user decided on 2026-10-06:
- the installation has a default brand, and a tenant may override it (5q: one codebase for SaaS and single-tenant);
- branding covers the **visual** (name, logo, favicon, colours, font) and the **assistant** (its name, greeting and
  tone). Custom domains, the sign-in page and e-mails come later;
- the operator **allows** white-labeling per tenant, and the tenant's admin **sets it up**.

## What Changes

- **The installation brand is core configuration** (12-factor: `Branding:*` settings, with assets in a mounted
  read-only directory). The core serves it at `GET /api/branding` to anyone, including before sign-in. It is not a
  plugin, because a tenant-scoped plugin cannot answer an anonymous request (thirteenth audit, blocker 1).
- **A tenant's override is the `branding` plugin** (app, tenant scope). Its allowance is the operator's
  white-labeling allowance, and its enablement and set-up belong to the tenant admin. It plugs into one core port,
  `IBrandProvider`, contributed through the `IContributesBrandProvider` seam published by `introduce-plugins` (at most one). The core checks that the
  plugin is in use for the tenant before it asks.
- **The brand sets the shell's existing tokens.** These are `--bg`, `--surface`, `--surface-2`, `--border`, `--text`,
  `--muted`, `--accent`, `--on-accent` and `--link`, each as `light-dark(l, d)` on `:root`. So every theme mode,
  including System, follows it, and the web-ui contrast pairs keep their meaning. The font defaults to today's
  `system-ui` stack.
- **Bounded fields, checked and hardened:**
  - **Contrast.** The web-ui spec's real token pairs are checked for WCAG 2.2 AA in both themes.
  - **Images.**
    - The logo and favicon are PNG or WebP. They are re-encoded on upload (SkiaSharp, MIT), which strips metadata and
      polyglots; animated WebP is refused.
    - They are served from a content-hash URL with the sniffed `Content-Type`, `Content-Disposition: inline` and
      `X-Content-Type-Options: nosniff`, and with an immutable ETag.
    - They are stored as blobs in the api's store through the plugin's model.
  - **Font.** One of a closed list of OFL fonts, shipped as woff2 files in the web module, so no CDN is used.
  - **Assistant name.** Shown by the UI only, and **never in the system prompt**.
  - **Greeting.** The chat's empty state, never a message, so it is never sent to the model.
  - **Tone.** One of formal, neutral or friendly. Each is a reviewed prompt fragment that reaches the core prompt
    through `IBrandProvider`.
- **A tone is a prompt change, so it is evaluated:** generation and selection run once per tone, and `tone` is
  recorded in the prompt trace event and in the eval settings.

## Capabilities

### New Capabilities

- `white-labeling`: the installation brand in core configuration, the tenant override plugin and its port, the
  bounded and hardened fields, and the tone's evaluation.

### Modified Capabilities

None. The tokens the brand sets already exist, and their contrast rule is reused as it is.

## Principles

- SOLID:
  - Single responsibility: the core renders and serves the installation brand, and the plugin stores tenant overrides.
  - Open/closed: a brand is data.
  - Interface segregation: `IBrandProvider` has one question, "the brand for this principal".
  - Dependency inversion: the core depends on that port, never on the plugin.
- Standards:
  - Theming: CSS custom properties with `light-dark()`; the token naming follows the Design Tokens Community Group
    Format Module (stable 2025.10, a W3C Community Group report, not a W3C Recommendation).
  - Accessibility: WCAG 2.2 AA.
  - Configuration: 12-factor.
  - Delivery and security: `X-Content-Type-Options: nosniff`; content-addressed immutable assets; image re-encoding
    against polyglots.
  - Prompt safety: closed choices only, and no tenant text in the prompt.
- Own: the brand object's shape (name, assets, the token values, greeting and tone). No standard application-brand
  schema exists; the DTCG format covers only the tokens, whose naming is kept. DECISIONS §88 (new, written when this
  change is applied).

## Progress

None — a save is one validated write. An upload (512 KB at most), its decode and re-encode, and the contrast checks
finish well under 3 seconds.

## Stopping

None — no long work starts. An upload is one request that the browser can abort, and nothing is stored until it is
complete, re-encoded and valid.

## Documentation impact

- `docs/plugins.md`: the `branding` plugin and `IBrandProvider`.
- `docs/http-api.md`: `GET /api/branding`, the asset routes and the admin routes.
- README: white-labeling. The screenshots are retaken (DECISIONS §55), since the header no longer says "maf-lab".
- DECISIONS §88 (new): `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies` for re-encoding (new pins in
  `Directory.Packages.props`); `System.Drawing.Common` (Windows-only) and ImageSharp (Six Labors Split License)
  rejected.
- The `data-lifecycle` inventory already lists the brand row (added there in advance).
