# Tasks

- [x] 1. Push main.
- [x] 2. Balancer: mount `compose/lb/`, start and reload with `-c`.
- [x] 3. Emulate forcing whenever more than one call is forced (a test with emulation off).
- [x] 4. Route portfolio data questions (`get_household_portfolio`, `get_aum_history`, one account id), and route
  only within the domains in scope (tests).
- [x] 5. Retrieval per domain:
  - a `domain` field on retrieval rows;
  - 15 portfolio rows;
  - the `portfolio-hybrid` variant and its thresholds;
  - the dataset check per corpus.
- [x] 6. Review queue: resolve per domain, a domain on the retrieval label, refuse mixed chunks (tests).
- [x] 7. Jev statistics: relevance per domain, the Domains section, the web section (tests).
- [x] 8. Domain eval: 16 more holdout rows.
- [ ] 9. Sharpen the domain descriptions against the measured errors; record before/after in DECISIONS.
- [ ] 10. Run retrieval, selection and domain evals; accept the baselines that moved.
- [ ] 11. Merge to main, `make` (the web container picks up the chat hints and the Jev page), verify live.
