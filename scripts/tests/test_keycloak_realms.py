"""Native realm contract regressions; mutating an obligation must be refused by the CLI gate."""
import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from keycloak_realms import RealmError, validate_realm


class RealmContracts(unittest.TestCase):
    def setUp(self):
        self.realm = json.loads((ROOT / "compose/keycloak/stage.realm.json").read_text())

    def clients(self):
        return {c["clientId"]: c for c in self.realm["clients"]}

    def scopes(self):
        return {s["name"]: s for s in self.realm["clientScopes"]}

    def test_reviewed_realms_and_exact_native_fixture_contract_pass(self):
        for name in ("stage", "prod", "fixture"):
            with self.subTest(name=name):
                realm = json.loads((ROOT / f"compose/keycloak/{name}.realm.json").read_text())
                self.assertTrue(validate_realm(realm, fixture=name == "fixture"))

    def test_public_web_never_gets_a_secret_or_non_PKCE_or_other_grants(self):
        changes = (("publicClient", False), ("secret", "fixture-secret"), ("implicitFlowEnabled", True),
                   ("directAccessGrantsEnabled", True), ("serviceAccountsEnabled", True))
        for field, value in changes:
            with self.subTest(field=field):
                realm = copy.deepcopy(self.realm)
                realm["clients"][0][field] = value
                with self.assertRaises(RealmError):
                    validate_realm(realm)
        self.clients()["web"]["attributes"]["pkce.code.challenge.method"] = "plain"
        with self.assertRaisesRegex(RealmError, "PKCE"):
            validate_realm(self.realm)

    def test_subject_scope_is_default_for_each_token_requester(self):
        for name in ("web", "api", "other-requester", "web-without-api"):
            with self.subTest(client=name):
                realm = json.loads((ROOT / "compose/keycloak/fixture.realm.json").read_text())
                client = next(c for c in realm["clients"] if c["clientId"] == name)
                client["defaultClientScopes"].remove("basic")
                client["optionalClientScopes"].append("basic")
                with self.assertRaisesRegex(RealmError, "Subject scope must be attached by default"):
                    validate_realm(realm, fixture=True)

    def test_subject_mapper_cannot_be_missing_disabled_or_replaced(self):
        for mutation in ("missing", "access", "introspection", "replacement"):
            with self.subTest(mutation=mutation):
                self.setUp()
                mappers = self.scopes()["basic"]["protocolMappers"]
                if mutation == "missing":
                    mappers.clear()
                elif mutation == "replacement":
                    mappers[0]["protocolMapper"] = "oidc-usermodel-property-mapper"
                    mappers[0]["config"].update({"user.attribute": "username", "claim.name": "sub"})
                else:
                    key = "access.token.claim" if mutation == "access" else "introspection.token.claim"
                    mappers[0]["config"][key] = "false"
                with self.assertRaisesRegex(RealmError, "[Ss]ubject mapper"):
                    validate_realm(self.realm)

    def test_subject_mapper_cannot_be_duplicated_by_effective_scope_or_client(self):
        for name in ("web", "api", "other-requester", "web-without-api"):
            for location in ("default", "optional", "client"):
                with self.subTest(client=name, location=location):
                    realm = json.loads((ROOT / "compose/keycloak/fixture.realm.json").read_text())
                    client = next(c for c in realm["clients"] if c["clientId"] == name)
                    basic = next(s for s in realm["clientScopes"] if s["name"] == "basic")
                    if location == "client":
                        client["protocolMappers"] = copy.deepcopy(basic["protocolMappers"])
                    else:
                        extra = copy.deepcopy(basic)
                        extra["name"] = "extra-subject"
                        realm["clientScopes"].append(extra)
                        client[location + "ClientScopes"].append(extra["name"])
                    with self.assertRaisesRegex(RealmError, "Effective subject mapper must be unique"):
                        validate_realm(realm, fixture=True)

    def test_subject_mapper_cannot_move_from_default_basic_to_optional_scope(self):
        subject = self.scopes()["basic"]["protocolMappers"].pop()
        self.realm["clientScopes"].append({"name": "optional-subject", "protocol": "openid-connect",
                                          "protocolMappers": [subject]})
        for name in ("web", "api"):
            self.clients()[name]["optionalClientScopes"].append("optional-subject")
        with self.assertRaisesRegex(RealmError, "Default basic scope must emit the subject"):
            validate_realm(self.realm)

    def test_native_clients_cannot_switch_protocols_while_retaining_oidc_options(self):
        for name in ("web", "api", "billing"):
            with self.subTest(client=name):
                self.setUp()
                self.clients()[name]["protocol"] = "saml"
                with self.assertRaisesRegex(RealmError, "OIDC protocol"):
                    validate_realm(self.realm)

    def test_redirects_and_origins_never_use_wildcards_or_unreviewed_origins(self):
        for value in ("https://other.example/*", "${MAF_WEB_ORIGIN}/*", "http://stage.example/auth/callback"):
            with self.subTest(value=value):
                realm = copy.deepcopy(self.realm)
                realm["clients"][0]["redirectUris"] = [value]
                with self.assertRaises(RealmError):
                    validate_realm(realm)
        self.clients()["web"]["webOrigins"] = ["*"]
        with self.assertRaises(RealmError):
            validate_realm(self.realm)

    def test_only_api_can_request_exchange_and_resources_have_no_login_flows(self):
        for field in ("standardFlowEnabled", "implicitFlowEnabled", "directAccessGrantsEnabled", "serviceAccountsEnabled"):
            with self.subTest(field=field):
                realm = copy.deepcopy(self.realm)
                realm["clients"][2][field] = True
                with self.assertRaises(RealmError):
                    validate_realm(realm)
        for name in ("web", "billing", "portfolio", "compliance"):
            for value in ("true", "TRUE", "TrUe"):
                with self.subTest(client=name, exchange=value):
                    self.setUp()
                    self.clients()[name].setdefault("attributes", {})["standard.token.exchange.enabled"] = value
                    with self.assertRaisesRegex(RealmError, "Only api"):
                        validate_realm(self.realm)

    def test_native_attribute_grants_cannot_bypass_code_only_or_resource_no_login_policy(self):
        for name in ("web", "api", "billing"):
            for grant in ("oauth2.device.authorization.grant.enabled", "oidc.ciba.grant.enabled",
                          "oauth2.jwt.authorization.grant.enabled"):
                for value in ("true", "TRUE"):
                    with self.subTest(client=name, grant=grant, value=value):
                        self.setUp()
                        self.clients()[name].setdefault("attributes", {})[grant] = value
                        with self.assertRaisesRegex(RealmError, grant):
                            validate_realm(self.realm)
                self.clients()[name]["attributes"][grant] = "false"
                self.assertTrue(validate_realm(self.realm))

    def test_web_api_and_resource_audiences_and_role_scopes_are_exact(self):
        self.scopes()["api-audience"]["protocolMappers"][0]["config"]["included.client.audience"] = "billing"
        with self.assertRaisesRegex(RealmError, "Web tokens"):
            validate_realm(self.realm)
        self.setUp()
        self.clients()["web"]["optionalClientScopes"].append("plugin-audiences")
        with self.assertRaisesRegex(RealmError, "Web cannot"):
            validate_realm(self.realm)
        self.setUp()
        self.scopes()["plugin-audiences"]["protocolMappers"].pop()
        with self.assertRaisesRegex(RealmError, "resource clients"):
            validate_realm(self.realm)
        self.setUp()
        self.realm["scopeMappings"] = []
        with self.assertRaisesRegex(RealmError, "role scope"):
            validate_realm(self.realm)

    def test_claim_attributes_are_admin_only_and_groups_are_ids_instead_of_paths(self):
        component = self.realm["components"]["org.keycloak.userprofile.UserProfileProvider"][0]
        profile = json.loads(component["config"]["kc.user.profile.config"][0])
        attribute = next(a for a in profile["attributes"] if a["name"] == "group_ids")
        attribute["permissions"]["edit"] = ["admin", "user"]
        component["config"]["kc.user.profile.config"][0] = json.dumps(profile)
        with self.assertRaisesRegex(RealmError, "administrator-managed"):
            validate_realm(self.realm)
        self.setUp()
        self.scopes()["domain-claims"]["protocolMappers"].append({"name": "paths", "protocol": "openid-connect", "protocolMapper": "oidc-group-membership-mapper", "config": {"claim.name": "groups"}})
        with self.assertRaisesRegex(RealmError, "Group paths"):
            validate_realm(self.realm)

    def test_extra_scope_or_client_role_mappings_cannot_add_core_privileges(self):
        for target in ({"clientScope": "profile"}, {"client": "web"}, {"client": "api"}):
            with self.subTest(target=target):
                self.setUp()
                self.realm["roles"]["realm"].append({"name": "PLATFORM_ADMIN"})
                self.realm["scopeMappings"].append(target | {"roles": ["PLATFORM_ADMIN"]})
                with self.assertRaisesRegex(RealmError, "reviewed roles scope"):
                    validate_realm(self.realm)
        self.setUp()
        self.realm["clientScopeMappings"] = {"api": [{"clientScope": "profile", "roles": ["PLATFORM_ADMIN"]}]}
        with self.assertRaisesRegex(RealmError, "client role scope"):
            validate_realm(self.realm)

    def test_core_role_composites_cannot_add_unreviewed_privileges(self):
        for name in ("TENANT_ADMIN", "USER", "READ_ONLY", "platform_operator"):
            with self.subTest(name=name):
                self.setUp()
                role = next(role for role in self.realm["roles"]["realm"] if role["name"] == name)
                role.update(composite=True, composites={"realm": ["PLATFORM_ADMIN"]})
                with self.assertRaisesRegex(RealmError, "composite privileges"):
                    validate_realm(self.realm)

    def test_extra_effective_web_audience_mappers_cannot_bypass_tenant_exchange(self):
        for location in ("scope", "client"):
            with self.subTest(location=location):
                self.setUp()
                mapper = {"name": "bypass", "protocol": "openid-connect", "protocolMapper": "oidc-audience-mapper",
                          "config": {"included.client.audience": "billing", "access.token.claim": "true"}}
                if location == "scope":
                    self.scopes()["domain-claims"]["protocolMappers"].append(mapper)
                else:
                    self.clients()["web"]["protocolMappers"] = [mapper]
                with self.assertRaisesRegex(RealmError, "Effective web/api audiences"):
                    validate_realm(self.realm)

    def test_extra_effective_mutable_privilege_sources_are_rejected(self):
        for location in ("scope", "client"):
            for claim in ("groups", "domain_roles", "advisor_ids"):
                with self.subTest(location=location, claim=claim):
                    self.setUp()
                    mapper = {"name": "bypass", "protocol": "openid-connect", "protocolMapper": "oidc-usermodel-attribute-mapper",
                              "config": {"user.attribute": "editable-note", "claim.name": claim, "multivalued": "true",
                                         "jsonType.label": "String", "aggregate.attrs": "false", "access.token.claim": "true"}}
                    if location == "scope":
                        self.scopes()["profile"]["protocolMappers"].append(mapper)
                    else:
                        self.clients()["api"]["protocolMappers"] = [mapper]
                    with self.assertRaisesRegex(RealmError, "Effective privilege mapper"):
                        validate_realm(self.realm)

    def test_fixture_ids_membership_and_profile_are_native_and_separate_from_prod(self):
        fixture = json.loads((ROOT / "compose/keycloak/fixture.realm.json").read_text())
        users = {u["username"]: u for u in fixture["users"]}
        for org in fixture["organizations"]:
            member = org["members"][0]
            ids = {g["id"] for g in org["groups"]}
            self.assertEqual("UNMANAGED", member["membershipType"])
            self.assertEqual(ids, set(member["groups"]))
            expected = {group for organization in fixture["organizations"] for membership in organization["members"]
                        if membership["username"] == member["username"] for group in membership["groups"]}
            self.assertEqual(expected, set(users[member["username"]]["attributes"]["group_ids"]))
        self.assertNotIn("users", self.realm)
        self.assertNotIn("organizations", self.realm)
        self.assertEqual("${MAF_API_CLIENT_SECRET}", self.clients()["api"]["secret"])
