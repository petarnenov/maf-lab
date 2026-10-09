"""Security contracts over native Keycloak realm templates; no import or credential expansion."""
import json

CORE_ROLES = {"TENANT_ADMIN", "USER", "READ_ONLY", "platform_operator"}


class RealmError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise RealmError(message)


def index(items, key):
    require(isinstance(items, list), "Native realm collection must be an array.")
    result = {}
    for item in items:
        require(isinstance(item, dict) and isinstance(item.get(key), str), "Native realm entry has no identifier.")
        require(item[key] not in result, "Native realm identifiers must be unique.")
        result[item[key]] = item
    return result


def validate_realm(realm, *, fixture=False):
    require(isinstance(realm, dict) and realm.get("enabled") is True and realm.get("organizationsEnabled") is True,
            "Realm must enable Organizations.")
    require(fixture or realm.get("sslRequired") in ("external", "all"), "Production realm must require TLS.")
    require(realm.get("defaultSignatureAlgorithm") == "RS256", "Realm must use its pinned asymmetric signature algorithm.")
    clients = index(realm.get("clients"), "clientId")
    scopes = index(realm.get("clientScopes"), "name")
    require("web" in clients and "api" in clients, "Realm requires the public web and confidential api clients.")
    web, api = clients["web"], clients["api"]
    require(web.get("publicClient") is True and web.get("standardFlowEnabled") is True, "Web client must use public authorization code flow.")
    require(web.get("attributes", {}).get("pkce.code.challenge.method") == "S256", "Web client must require PKCE S256.")
    require("secret" not in web, "Public web client cannot contain a secret.")
    require(not web.get("implicitFlowEnabled") and not web.get("directAccessGrantsEnabled") and not web.get("serviceAccountsEnabled"),
            "Web client may use authorization code only.")
    redirects = web.get("redirectUris", [])
    origins = web.get("webOrigins", [])
    require(len(redirects) == 1 and isinstance(redirects[0], str) and redirects[0].endswith("/auth/callback")
            and "*" not in redirects[0], "Web redirect must be one exact callback URI.")
    origin = redirects[0][:-len("/auth/callback")]
    require(origins == [origin] and (fixture or origin.startswith("https://") or origin == "${MAF_WEB_ORIGIN}"),
            "Web origin must be exact and use reviewed HTTPS configuration.")
    require(web.get("attributes", {}).get("post.logout.redirect.uris") == origin + "/auth/signed-out", "Logout redirect must be exact.")
    require(api.get("publicClient") is False and api.get("clientAuthenticatorType") == "client-secret"
            and isinstance(api.get("secret"), str) and bool(api["secret"]), "Api requester must be confidential.")
    require(api.get("attributes", {}).get("standard.token.exchange.enabled") == "true", "Api requester must enable supported V2 exchange.")
    require(not any(api.get(key) for key in ("standardFlowEnabled", "implicitFlowEnabled", "directAccessGrantsEnabled", "serviceAccountsEnabled")),
            "Api requester cannot enable an unrelated login flow.")
    for name, client in clients.items():
        require(client.get("protocol") == "openid-connect", "Reviewed clients must use the OIDC protocol.")
        require(client.get("fullScopeAllowed") is False, "Client scope mappings must be explicit.")
        attributes = client.get("attributes", {})
        require(isinstance(attributes, dict), "Client attributes must be native options.")
        for grant in ("oauth2.device.authorization.grant.enabled", "oidc.ciba.grant.enabled",
                      "oauth2.jwt.authorization.grant.enabled"):
            value = attributes.get(grant)
            require(value is None or isinstance(value, str) and value.lower() == "false",
                    f"Client may not enable the unrelated native grant {grant}.")
        if name != "api" and not fixture:
            exchange = attributes.get("standard.token.exchange.enabled")
            require(exchange is None or isinstance(exchange, str) and exchange.lower() == "false",
                    "Only api may request exchange.")
        if name not in ("web", "api") and not fixture:
            require(client.get("publicClient") is False and not any(client.get(key) for key in
                    ("standardFlowEnabled", "implicitFlowEnabled", "directAccessGrantsEnabled", "serviceAccountsEnabled")),
                    "Resource clients must be confidential without a login flow.")
    for client, required in ((web, {"roles", "api-audience", "domain-claims"}), (api, {"roles", "plugin-audiences", "domain-claims"})):
        require(required <= set(client.get("defaultClientScopes", [])), "Client is missing an effective security scope.")
        require("organization" in client.get("optionalClientScopes", []), "Client requires the organization scope.")
    for name in ("basic", "roles", "api-audience", "plugin-audiences", "domain-claims", "organization"):
        require(name in scopes and scopes[name].get("protocol") == "openid-connect", "Security scope must use OIDC.")
        for mapper in scopes[name].get("protocolMappers", []):
            require(mapper.get("protocol") == "openid-connect" and isinstance(mapper.get("config"), dict)
                    and all(isinstance(value, str) for value in mapper["config"].values()), "Protocol mapper options must be native strings.")
    org = scopes["organization"].get("protocolMappers", [])
    require(len(org) == 1 and org[0].get("protocolMapper") == "oidc-organization-membership-mapper"
            and org[0]["config"].get("addOrganizationId") == "true" and org[0]["config"].get("access.token.claim") == "true",
            "Organization scope requires its native membership mapper.")
    def audiences(scope):
        return {mapper["config"].get("included.client.audience") for mapper in scopes[scope].get("protocolMappers", [])
                if mapper.get("protocolMapper") == "oidc-audience-mapper" and mapper["config"].get("access.token.claim") == "true"}
    require(audiences("api-audience") == {"api"}, "Web tokens must carry api and no plugin audience.")
    require("plugin-audiences" not in web.get("defaultClientScopes", []) + web.get("optionalClientScopes", []), "Web cannot request plugin audiences.")
    resources = set(clients) - {"web", "api", "other-requester", "web-without-api"}
    require(audiences("plugin-audiences") == resources, "Api audience mappers must cover exactly the resource clients.")
    # Evaluate every default/optional scope and client-local mapper; checking a named scope alone permits bypasses.
    def effective_mappers(client):
        names = client.get("defaultClientScopes", []) + client.get("optionalClientScopes", [])
        require(all(name in scopes for name in names), "Effective client scopes must be declared and reviewed.")
        return client.get("protocolMappers", []) + [mapper for name in names for mapper in scopes[name].get("protocolMappers", [])]
    # Keycloak 26.8 emits access-token sub through its native basic-scope mapper.
    subject_clients = {"web", "api"} | ({"other-requester", "web-without-api"} & set(clients) if fixture else set())
    for name in subject_clients:
        client = clients[name]
        require("basic" in client.get("defaultClientScopes", []), "Subject scope must be attached by default.")
        subjects = [mapper for mapper in effective_mappers(client) if mapper.get("protocolMapper") == "oidc-sub-mapper"]
        require(len(subjects) == 1, "Effective subject mapper must be unique.")
        subject = subjects[0]
        require(subject.get("protocol") == "openid-connect" and subject.get("config") == {
                    "access.token.claim": "true", "introspection.token.claim": "true"},
                "Subject mapper must use the native access-token and introspection configuration.")
        require(subject in scopes["basic"].get("protocolMappers", []), "Default basic scope must emit the subject.")
    protected = {"groups": "group_ids", "domain_roles": "domain_roles", "advisor_ids": "advisor_ids"}
    for name, client, expected_audiences in (("web", web, {"api"}), ("api", api, resources)):
        emitted = set()
        protected_counts = {claim: 0 for claim in protected}
        organizations = roles = 0
        for mapper in effective_mappers(client):
            require(isinstance(mapper, dict) and mapper.get("protocol") == "openid-connect" and isinstance(mapper.get("config"), dict)
                    and all(isinstance(value, str) for value in mapper["config"].values()), "Effective protocol mapper options must be native strings.")
            kind, config = mapper.get("protocolMapper"), mapper["config"]
            claim = config.get("claim.name", "")
            if kind == "oidc-sub-mapper":
                continue  # Native subject configuration and uniqueness are checked above.
            elif kind == "oidc-audience-mapper":
                require(config.get("access.token.claim") == "true" and not config.get("included.custom.audience"), "Effective audience mapper must be explicit.")
                emitted.add(config.get("included.client.audience"))
            elif kind == "oidc-usermodel-attribute-mapper":
                require(claim in protected and config.get("user.attribute") == protected[claim]
                        and config.get("aggregate.attrs") == "false" and config.get("multivalued") == "true"
                        and config.get("jsonType.label") == "String" and config.get("access.token.claim") == "true",
                        "Effective privilege mapper must use the protected provisioner-managed attribute.")
                protected_counts[claim] += 1
            elif kind == "oidc-organization-membership-mapper":
                require(claim == "organization" and config.get("access.token.claim") == "true", "Effective organization mapper must be native.")
                organizations += 1
            elif kind == "oidc-usermodel-realm-role-mapper":
                require(claim == "realm_access.roles" and config.get("access.token.claim") == "true" and config.get("multivalued") == "true"
                        and config.get("jsonType.label") == "String", "Effective role mapper must use controlled realm roles.")
                roles += 1
            else:
                require(kind not in ("oidc-group-membership-mapper", "oidc-organization-group-membership-mapper"), "Group paths cannot substitute for provisioned ACL group IDs.")
                require(kind == "oidc-full-name-mapper", "Unreviewed effective mapper may alter identity or audiences.")
        require(emitted == expected_audiences, "Effective web/api audiences cannot include an unauthorized resource.")
        require(organizations == 1 and roles == 1 and all(count == 1 for count in protected_counts.values()),
                "Effective identity and privilege mappers must be unique.")
    # Native access calculation unions role mappings from every effective scope and the client itself.
    # A reviewed roles mapper alone cannot prevent an extra profile/client mapping from emitting privileges.
    role_map = realm.get("scopeMappings", [])
    require(isinstance(role_map, list) and len(role_map) == 1 and isinstance(role_map[0], dict)
            and set(role_map[0]) == {"clientScope", "roles"} and role_map[0]["clientScope"] == "roles"
            and isinstance(role_map[0]["roles"], list) and set(role_map[0]["roles"]) == CORE_ROLES,
            "Effective core role scope mappings must use only the reviewed roles scope.")
    require(not realm.get("clientScopeMappings"), "Unreviewed client role scope mappings cannot add privileges.")
    realm_roles = index(realm.get("roles", {}).get("realm", []), "name")
    require(CORE_ROLES <= set(realm_roles) and all(not realm_roles[name].get("composite")
            and not realm_roles[name].get("composites") for name in CORE_ROLES),
            "Core realm roles must be declared without composite privileges.")
    components = realm.get("components", {}).get("org.keycloak.userprofile.UserProfileProvider", [])
    require(len(components) == 1 and components[0].get("providerId") == "declarative-user-profile", "Native User Profile protection is required.")
    raw = components[0].get("config", {}).get("kc.user.profile.config", [])
    require(isinstance(raw, list) and len(raw) == 1 and isinstance(raw[0], str), "User Profile is one native JSON-string configuration.")
    try:
        attributes = index(json.loads(raw[0])["attributes"], "name")
    except (ValueError, KeyError, TypeError):
        raise RealmError("Invalid User Profile configuration.") from None
    require({"username", "email", "firstName", "lastName"} <= set(attributes), "Built-in User Profile attributes must remain available.")
    claims = scopes["domain-claims"].get("protocolMappers", [])
    for attribute, claim in (("group_ids", "groups"), ("domain_roles", "domain_roles"), ("advisor_ids", "advisor_ids")):
        require(attributes.get(attribute, {}).get("permissions") == {"view": ["admin"], "edit": ["admin"]},
                "Security attributes must be administrator-managed.")
        matching = [m for m in claims if m.get("protocolMapper") == "oidc-usermodel-attribute-mapper"
                    and m["config"].get("user.attribute") == attribute and m["config"].get("claim.name") == claim]
        require(len(matching) == 1 and matching[0]["config"].get("aggregate.attrs") == "false"
                and matching[0]["config"].get("multivalued") == "true" and matching[0]["config"].get("access.token.claim") == "true",
                "Provisioned security claim mapper must preserve controlled values.")
    require(not any(m.get("protocolMapper") in ("oidc-group-membership-mapper", "oidc-organization-group-membership-mapper")
                    for scope in scopes.values() for m in scope.get("protocolMappers", [])),
            "Group paths cannot substitute for provisioned ACL group IDs.")
    return True
