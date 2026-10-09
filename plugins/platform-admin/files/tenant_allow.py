#!/usr/bin/env python3
"""One allowance write using the tenant's operator credential, without printing it."""
from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts"))
from tenant_plugins_bootstrap import RequestError, Stop, allow_plugin, operator_token, stopping  # noqa: E402


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--tenant", required=True)
    parser.add_argument("--plugin", required=True)
    parser.add_argument("--enable", choices=("0", "1"), default="0")
    args = parser.parse_args(argv)
    if not re.fullmatch(r"firm-[a-z0-9][a-z0-9-]{0,62}", args.tenant):
        parser.error("TENANT must be an organization such as firm-a (shared is not an organization).")
    if not re.fullmatch(r"[a-z_][a-z0-9_-]*", args.plugin):
        parser.error("PLUGIN must be a plugin name.")
    stop = Stop()
    try:
        with stopping(stop):
            supplied_token = os.environ.get("MAF_BEARER_TOKEN")
            token = supplied_token or operator_token(args.base_url, args.tenant)
            if not stop.requested:
                allow_plugin(args.base_url, args.plugin, token, enable=args.enable == "1")
        if stop.requested:
            print("Stopped after the current request; the write is idempotent, rerun make tenant-allow.", file=sys.stderr)
            return 130
        organization = "operator organization" if supplied_token else args.tenant
        print(f"{organization}: {args.plugin} allowed" + (" and enabled" if args.enable == "1" else ""))
        return 0
    except RequestError as error:
        if stop.requested:
            print("Stopped after the current request; its outcome is unknown, rerun make tenant-allow.", file=sys.stderr)
            return 130
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
