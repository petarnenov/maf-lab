"""Production identity settings must reach every service that validates a resource token."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class CompanyIdentityComposition(unittest.TestCase):
    def test_api_and_each_resource_receive_environment_authority_and_role_client(self):
        for path in ("compose/docker-compose.yml", "plugins/billing/compose.yml", "plugins/portfolio/compose.yml", "plugins/code/compose.yml"):
            with self.subTest(path=path):
                source = (ROOT / path).read_text()
                self.assertIn("      MAF_ENV: ${MAF_ENV:-dev}", source)
                self.assertIn("      Auth__Authority: ${AUTH_AUTHORITY:-}", source)
                self.assertIn("      Auth__CoreRoleClientId: ${AUTH_CORE_ROLE_CLIENT_ID:-api}", source)

    def test_composition_roots_use_the_company_or_dev_authentication_boundary(self):
        for path, audience in (("src/Maf.Lab.Api/Program.cs", None), ("src/Maf.Lab.Retrieval/Program.cs", "billing"),
                               ("src/Maf.Lab.Portfolio/Program.cs", "portfolio"), ("plugins/code/service/Program.cs", "code")):
            with self.subTest(path=path):
                source = (ROOT / path).read_text()
                expected = 'AddLabAuthentication(builder.Configuration' + (f', "{audience}"' if audience else '') + ')'
                self.assertIn(expected, source)
                self.assertNotIn("AddDevJwtAuthentication(", source)

    def test_model_free_ci_cannot_inherit_a_company_authority(self):
        source = (ROOT / "Makefile").read_text()
        ci = re.search(r"ifeq \(\$\(CI_MODE\),1\)\n(.*?)\nendif", source, re.S)
        self.assertIsNotNone(ci)
        self.assertIn("export AUTH_AUTHORITY :=", ci.group(1))

    def test_each_resource_has_a_separate_public_uri_and_explicit_proxy_trust(self):
        for name in ("billing", "portfolio", "code"):
            with self.subTest(resource=name):
                source = (ROOT / f"plugins/{name}/compose.yml").read_text()
                self.assertIn(f"Auth__ResourceUri: ${{{name.upper()}_MCP_RESOURCE_URI:-}}", source)
                self.assertIn("Auth__TrustedProxyNetworks__0: ${MCP_TRUSTED_PROXY_NETWORK:-}", source)

    def test_metadata_routes_preserve_sdk_paths_and_uninstalled_routes_are_reserved(self):
        for name, path, pool in (("billing", "mcp", "mcp_pool"), ("portfolio", "portfolio/mcp", "mcp_portfolio_pool"),
                                 ("code", "code/mcp", "mcp_code_pool")):
            with self.subTest(resource=name):
                source = (ROOT / f"plugins/{name}/lb.server.conf").read_text()
                block = re.search(r"location = /\.well-known/oauth-protected-resource/" + path + r"\s*\{([^}]+)\}", source)
                self.assertIsNotNone(block)
                self.assertIn(f"proxy_pass http://{pool};", block.group(1))
                self.assertNotIn("proxy_pass http://" + pool + "/", block.group(1))
        self.assertRegex((ROOT / "compose/lb/nginx.conf").read_text(),
                         r"location \^~ /\.well-known/oauth-protected-resource\s*\{\s*return 404;")

    def test_resource_roots_install_native_metadata_and_process_trusted_forwarding_before_auth(self):
        for path in ("src/Maf.Lab.Retrieval/Program.cs", "src/Maf.Lab.Portfolio/Program.cs", "plugins/code/service/Program.cs"):
            with self.subTest(path=path):
                source = (ROOT / path).read_text()
                self.assertIn("AddLabMcpAuthentication(builder.Configuration)", source)
                self.assertLess(source.index("app.UseLabMcpForwarding();"), source.index("app.UseAuthentication();"))
        self.assertNotIn("AddLabMcpAuthentication", (ROOT / "src/Maf.Lab.Api/Program.cs").read_text())

    def test_each_actual_mcp_host_checks_content_permission_and_requires_the_shared_store(self):
        for path in ("src/Maf.Lab.Retrieval/Program.cs", "src/Maf.Lab.Portfolio/Program.cs", "plugins/code/service/Program.cs"):
            with self.subTest(path=path):
                source = (ROOT / path).read_text()
                self.assertIn(".WithOperatorContentAccess()", source)
                self.assertIn("RequireSharedState<Maf.Lab.Domain.SharedState.IBreakGlassPermissionStore>()", source)
                self.assertIn("builder.AddSharedState();", source)
        for name in ("portfolio", "code"):
            self.assertRegex((ROOT / f"plugins/{name}/compose.yml").read_text(),
                             r"depends_on:\s+redis:\s+condition: service_healthy")

    def test_permission_revocation_uses_synchronous_persistence_in_the_declared_redis_deployment(self):
        source = (ROOT / "compose/docker-compose.yml").read_text()
        self.assertIn('"--appendonly", "yes", "--appendfsync", "always"', source)
