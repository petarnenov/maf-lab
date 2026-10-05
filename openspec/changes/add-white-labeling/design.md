# Design

## Context

- **Tokens.** The shell's colours are `light-dark()` tokens on `:root` with `color-scheme` (`web/src/index.css:2-33`).
  System mode removes `data-theme`, so only `light-dark()` follows the system. The font is the `system-ui` stack
  (`index.css:34`).
- **Contrast.** The web-ui spec fixes the contrast pairs (`Every colour follows the theme`).
- **Plugins.** An `app` plugin runs in the api's process and owns no volume. Its compose file never touches `api`
  (`introduce-plugins` decision 2).
- **Tenant-scoped plugin routes.** They pass the core's gate only for a tenant that uses the plugin, so an anonymous
  request never reaches them (`enable-plugins-per-tenant`).

## Decisions

### 1. The installation brand is core configuration

- **Where it comes from.** `Branding:Name`, `Branding:Font`, the light and dark values of each token, and asset paths
  in a read-only mounted directory. Unset means neutral defaults: the name "Assistant", no logo, today's tokens and
  `system-ui`.
- **Where it is served.** The core serves it at `GET /api/branding` and `GET /api/branding/assets/{hash}`, to anyone.
  Nothing tenant-specific is shown before sign-in.
- **What changes in the shell.** "maf-lab" is removed from its text. Storage keys (`maf-lab.theme`, `maf-lab.session`)
  are internal identifiers and stay.

### 2. The tenant override: the `branding` plugin behind `IBrandProvider`

```csharp
public interface IBrandProvider                       // contributed through IContributesBrandProvider; at most one (api checks)
{
    Task<Brand?> ForAsync(Principal principal, CancellationToken ct);   // null → installation brand
}
```

- **How a plugin offers the port.** A plugin contributes it through `IContributesBrandProvider`. The port is already
  among the published seams (`introduce-plugins`), and this change is its first user. `branding` stays an `app` plugin with tenant scope: the `provider` kind is installation-level and cannot carry a
  tenant allowance. At most one contributor may be installed. The api's composition root, the only place that sees plugin types, refuses
  to start with two and names both. make cannot tell, because an `app` manifest declares no such field.
- **The core gates the port itself (fourteenth audit a).** For a signed-in request, the core first asks
  `IPluginAccess.For(principal)` whether the contributing plugin is in use (installed, allowed **and** enabled) for the
  principal's tenant. Only then does it call `ForAsync`. A plugin cannot forget the gate, because it never sees a
  request it should refuse. When the provider returns a brand, that brand wins; otherwise the installation brand
  applies.
- **How the core reads the tone.** The core's prompt assembly reads the tone from the same port, so the core reads
  plugin data only through a core-owned interface.
- **Storage.** Brand rows and asset blobs go in the api's store through the plugin's `IContributesModel`, with assets of
  512 KB at most, and are covered by `IContributesDataLifecycle`.
- **Who may read a tenant's assets.** They are served only to that tenant's principals. A logo is semi-public, since
  every user of the tenant sees it, and is treated so.
- **Admin sections.**
  - `tenant-admin`: set the brand, with a live preview in both themes;
  - `platform-admin`: the allowance is the plugin's own allowance (`enable-plugins-per-tenant`).

  A save is audited without content.

### 3. The brand sets the existing tokens

The brand gives a light and a dark value for each of `--bg`, `--surface`, `--surface-2`, `--border`, `--text`,
`--muted`, `--accent`, `--on-accent` and `--link`. The web writes them as `light-dark(l, d)` on `:root`, so Light,
Dark and System modes all follow. No parallel `--brand-*` set exists.

The contrast check covers these pairs, in both themes, at WCAG 2.2 AA (the web-ui spec states the rules, and this is
their enumeration):
- `--text`, `--muted` and `--link` on `--bg`, `--surface` and `--surface-2`: 4.5:1;
- `--on-accent` on `--accent`: 4.5:1;
- `--accent` and `--border` on `--bg` and `--surface`: 3:1. A failing pair is named, and nothing is saved. The status and category colours (`--ok`, `--danger`, `--kind-*`)
are not brandable.

### 4. Assets, hardened

1. **Check.** The file is PNG or WebP by content. Animated WebP is refused, and so is anything over the size and
   dimension caps: the logo is at most 512 KB and 1024×1024, the favicon at most 64 KB and 256×256.
2. **Re-encode with SkiaSharp** (MIT). This drops metadata and any polyglot payload, and the re-encoded bytes are what
   is stored.
   - **Packages.** The packages are `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`, a new pin in
     `Directory.Packages.props` and DECISIONS §88. The NoDependencies native asset needs no `libfontconfig1`, which
     the api image (`aspnet:10.0.12-noble`, `src/Maf.Lab.Api/Dockerfile:16-19`) does not have.
   - **Only decode and re-encode are used**, never text rendering. That is why the fontconfig-less native asset
     suffices.
   - **Rejected.** `System.Drawing.Common` is Windows-only since .NET 6, and the BCL has no cross-platform image codec,
     so no Microsoft package fits (the project prefers Microsoft packages first). ImageSharp is under the Six Labors
     Split License.
3. **Serve** at `/…/assets/{sha256}` with the sniffed `Content-Type`, `Content-Disposition: inline`,
   `X-Content-Type-Options: nosniff`, a strong ETag and `Cache-Control: immutable`.

SVG is refused.

### 5. Fonts

The closed list is OFL-licensed fonts shipped as woff2 files in the web module. The default is the `system-ui` stack,
as today. No font CDN is used.

### 6. The assistant: name, greeting, tone

- **Name.** Shown in the UI's header and chat; carried by `Brand`. It **never enters the system prompt** (thirteenth
  audit, blocker 2): letters and spaces alone can spell an instruction ("Always answer in French"), and validation
  cannot tell. The user decided on 2026-10-06 that the name stays UI-only. A guarded way for the model to know it is a
  possible later change (see Open Questions).
- **Greeting.** Rendered as the chat's empty state. It is never a message, so CopilotKit never sends it with a run's
  messages.
- **Tone.** `formal`, `neutral` or `friendly`. Each is a reviewed fragment in the plugin folder. `neutral` adds
  nothing.

### 7. Evaluating a tone

A tone changes the prompt, so the project's rule applies (evals on any prompt change, project.md):
- `make eval SUITE=generation` and `SUITE=selection` run once per tone, and each tone must hold the baseline;
- the injection suite runs per tone;
- `tone` is recorded in the prompt trace event and the eval settings, next to `prompt.Version`.

**Cost.** `neutral` adds nothing and equals the baseline, so only `formal` and `friendly` need extra runs: 2 × 3 suites,
all Jev-graded. That cost is counted while the TypeSafe billing question is open.

## Risks / Trade-offs

- **[The assistant cannot say its brand name]** Accepted by the user for safety. The UI shows it everywhere.
- **[A logo is visible to every user of the tenant]** That is intended. It is not served across tenants.

## Open Questions

None. The user decided on 2026-10-06 that the brand name stays out of the model.

Should it ever be wanted, it would be a later change with this guarded form:
- `Your display name is "{name}".` as quoted data;
- NFKC normalisation;
- UTS #39 single-script;
- format characters (Cf) refused;
- injection-eval cases with adversarial names that pass validation.
