# Tasks

## 1. Web

- [x] 1.1 Add the `--kind-*` tokens and `--warn` to `index.css` (design §2). Point `kindColor`, `frameColor` and
  `domainColor` at them. Verify with the existing monitor tests and a `kindColors` test that every kind resolves to a
  token.
- [x] 1.2 Move the remaining fixed colours onto tokens:
  - `--on-accent` for text on fills (monitor kind badge, domain badge, Approve, Delete);
  - `--warn` for Topology's degraded state;
  - category tokens for the monitor's system and tool message borders;
  - `--accent` for the current-step outline and the telemetry bars.
  
  Verify that `grep` finds no hex or `rgb()` colour in `src/**/*.css` outside `index.css`, the chart tokens and the
  scrims.

## 2. Verification

- [x] 2.1 Run `make test`, `make lint` and `make build-web`; all green.
- [x] 2.2 Rebuild with `make`. At http://localhost:7171 in the dark theme, check the computed colours: the monitor
  timeline, the Domains tab, Topology, the Approve and Delete buttons.
- [x] 2.3 Run `openspec validate fix-theme-colors --strict`; valid.
