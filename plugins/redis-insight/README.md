# redis-insight

[Redis Insight](https://redis.io/insight/) on http://localhost:7174, a developer tool allowed in dev and qa (`make plugin-on NAME=redis-insight`, `make plugin-off NAME=redis-insight`).
It is linked from the web UI's navigation and published on `127.0.0.1` only.

It opens on the `maf-lab` database: run state, stops and the shared stores. It has no login and can change or delete
keys: it is a window onto dev state, not a tool for anything you want to keep.
