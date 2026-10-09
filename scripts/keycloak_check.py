#!/usr/bin/env python3
"""Check the reviewed external IdP bundle; never import a realm or contact an IdP.

Feature settings are native server build options, not RealmRepresentation fields.
Only native keycloak.conf, KC_FEATURES/KC_FEATURES_DISABLED/KC_FEATURE_* and the
equivalent list CLI overrides are in scope. The caller must pass the same options
to its server build; this gate does not inspect an already-built server image.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import signal
import sys

from keycloak_realms import RealmError, validate_realm
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "26.8.0"
POLICY_PATH = ROOT / "compose/keycloak/feature-policy.json"


class CheckError(Exception):
    """Safe diagnostic: option names, never secret values or realm contents."""


def read_text(path):
    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeError):
        raise CheckError(f"Cannot read {path.name}.") from None


def read_json(path):
    try:
        return json.loads(read_text(path))
    except ValueError:
        raise CheckError(f"Invalid JSON in {path.name}.") from None


def configuration(path):
    values = {}
    for line in read_text(path).splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        name, sep, value = line.partition("=")
        name = name.strip()
        if not sep or not re.fullmatch(r"[a-z][a-z0-9-]*", name):
            raise CheckError(f"Invalid native configuration in {path.name}.")
        if name in values:
            raise CheckError(f"Duplicate option {name} in {path.name}.")
        values[name] = value.strip()
    return values


def feature_fields(document):
    for key in document:
        normalized = key.lower().replace("_", "-")
        if normalized in ("features", "features-disabled", "kc-features", "kc-features-disabled") or normalized.startswith(("feature-", "kc-feature-")):
            raise CheckError(f"Realm contains unsupported server feature field {key}; use keycloak.conf.")


def list_tokens(value, option, policy, *, disabled=False):
    if not value:
        return []
    tokens = value.split(",")
    for token in tokens:
        # Never echo arbitrary option values (they may have come from a secret).
        if not re.fullmatch(r"[a-z][a-z0-9-]*(?::v[1-9][0-9]*)?", token):
            raise CheckError(f"Invalid feature identifier in {option}.")
        name, _, version = token.partition(":")
        if name == "preview":
            if version:
                raise CheckError("Unsupported version for preview.")
            if disabled:
                continue
            raise CheckError("Unsupported feature preview.")
        if name not in policy["features"]:
            raise CheckError(f"Unknown feature {name} in {option}.")
        if disabled and version:
            raise CheckError(f"Disabled feature {name} must be unversioned.")
        if version and version not in policy["features"][name]["versions"]:
            raise CheckError(f"Unsupported version for feature {name}.")
    if len(tokens) != len(set(tokens)):
        raise CheckError(f"Duplicate feature in {option}.")
    return tokens


def substitution(value, environment, option):
    def replace(match):
        name, default = match.group(1), match.group(2)
        resolved = environment.get(name, default)
        if resolved is None:
            raise CheckError(f"Unresolved environment substitution in {option}.")
        return resolved
    value = re.sub(r"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::([^}]*))?\}", replace, value)
    if "$" in value:
        raise CheckError(f"Unsupported environment substitution in {option}.")
    return value


def effective_features(config, environment, policy, *, cli_features=None, cli_features_disabled=None):
    enabled = environment.get("KC_FEATURES", config.get("features", ""))
    disabled = environment.get("KC_FEATURES_DISABLED", config.get("features-disabled", ""))
    if cli_features is not None:
        enabled = cli_features
    if cli_features_disabled is not None:
        disabled = cli_features_disabled
    enabled = list_tokens(substitution(enabled, environment, "features"), "features", policy)
    disabled = list_tokens(substitution(disabled, environment, "features-disabled"), "features-disabled", policy, disabled=True)
    singles = {}
    for option, value in config.items():
        if option.startswith("feature-"):
            singles[option.removeprefix("feature-")] = substitution(value, environment, option)
    for option, value in environment.items():
        if option.startswith("KC_FEATURE_"):
            singles[option.removeprefix("KC_FEATURE_").lower().replace("_", "-")] = value
    for name in singles:
        if name not in policy["features"]:
            raise CheckError(f"Unknown feature {name} in individual feature option.")
    by_name = {}
    for token in enabled:
        name, _, version = token.partition(":")
        if name in by_name:
            raise CheckError(f"Multiple enabled versions of feature {name}.")
        by_name[name] = version or None
    selected = {}
    for name, item in policy["features"].items():
        single = singles.get(name)
        if single is not None:
            # Keycloak's individual feature option overrides both list options.
            if single not in ("enabled", "disabled") and single not in item["versions"]:
                raise CheckError(f"Invalid individual feature option for {name}.")
            setting = single
        elif name in by_name and name in disabled:
            raise CheckError(f"Feature {name} is both enabled and disabled.")
        elif name in disabled:
            setting = "disabled"
        elif name in by_name:
            setting = by_name[name] or "enabled"
        else:
            setting = item.get("default", "disabled")
        if setting == "disabled":
            if item.get("essential"):
                raise CheckError(f"Essential feature {name} cannot be disabled.")
            continue
        version = item["preferred"] if setting == "enabled" else setting
        selected[name] = version
        status = item["versions"][version]
        if status != "supported":
            raise CheckError(f"Unsupported feature {name}:{version} ({status}).")
    for name, version in selected.items():
        for dependency in policy["features"][name].get("dependencies", {}).get(version, []):
            required, _, required_version = dependency.partition(":")
            if selected.get(required) != required_version:
                raise CheckError(f"Feature {name}:{version} requires enabled feature {dependency}.")
    for name in ("organization", "token-exchange-standard"):
        if name not in selected:
            raise CheckError(f"Required feature {name} is disabled.")
    return selected


def validate_bundle(bundle, environ=None, *, cli_features=None, cli_features_disabled=None):
    environ = dict(os.environ if environ is None else environ)
    if read_text(bundle / "VERSION").strip() != VERSION:
        raise CheckError(f"Keycloak bundle must pin version {VERSION}.")
    policy = read_json(POLICY_PATH)
    if policy.get("keycloak_version") != VERSION:
        raise CheckError("Feature policy version does not match the pinned Keycloak version.")
    results = {}
    for name in ("stage", "prod"):
        realm = read_json(bundle / f"{name}.realm.json")
        if not isinstance(realm, dict):
            raise CheckError(f"{name}.realm.json must contain a realm object.")
        feature_fields(realm)
        try:
            validate_realm(realm)
        except RealmError as error:
            raise CheckError(f"{name} realm: {error}") from None
        config = configuration(bundle / f"{name}.keycloak.conf")
        try:
            results[name] = effective_features(config, environ, policy, cli_features=cli_features,
                                              cli_features_disabled=cli_features_disabled)
        except CheckError as error:
            raise CheckError(f"{name}: {error}") from None
    return results


def interrupted(_signum, _frame):
    raise KeyboardInterrupt


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, default=ROOT / "compose/keycloak")
    parser.add_argument("--features", help="native --features build option override")
    parser.add_argument("--features-disabled", help="native --features-disabled build option override")
    args = parser.parse_args(argv)
    previous = {s: signal.signal(s, interrupted) for s in (signal.SIGINT, signal.SIGTERM)}
    try:
        validate_bundle(args.bundle, cli_features=args.features, cli_features_disabled=args.features_disabled)
        print(f"Keycloak {VERSION}: stage/prod bundle uses supported server features only.")
        return 0
    except KeyboardInterrupt:
        print("Keycloak check stopped.", file=sys.stderr)
        return 130
    except CheckError as error:
        print(f"Keycloak check failed: {error}", file=sys.stderr)
        return 1
    finally:
        for signum, handler in previous.items():
            signal.signal(signum, handler)


if __name__ == "__main__":
    sys.exit(main())
