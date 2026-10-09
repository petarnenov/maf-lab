using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>Maps validated Keycloak Organizations/roles to the core principal; never trusts legacy tenant claims.</summary>
public static class CompanyIdentityClaims
{
    public static bool TryNormalize(ClaimsPrincipal? user, string coreRoleClientId, out ClaimsPrincipal normalized)
    {
        normalized = null!;
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(coreRoleClientId)) return false;
        var subjects = user.FindAll(PrincipalClaims.UserId).ToArray();
        var organizations = user.FindAll("organization").ToArray();
        if (subjects.Length != 1 || subjects[0].ValueType != ClaimValueTypes.String
            || string.IsNullOrWhiteSpace(subjects[0].Value) || organizations.Length != 1
            || organizations[0].ValueType != "JSON") return false;
        try
        {
            using var json = JsonDocument.Parse(organizations[0].Value);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return false;
            var entries = json.RootElement.EnumerateObject().ToArray();
            if (entries.Length != 1 || entries[0].Value.ValueKind != JsonValueKind.Object
                || !TenantId.TryParse(entries[0].Name, out var tenant) || tenant.IsShared) return false;
            var roles = new HashSet<Role>();
            foreach (var claim in user.FindAll("realm_access"))
            {
                if (claim.ValueType != "JSON") return false;
                using var realm = JsonDocument.Parse(claim.Value);
                if (!ReadRoles(realm.RootElement, roles)) return false;
            }
            foreach (var claim in user.FindAll("resource_access"))
            {
                if (claim.ValueType != "JSON") return false;
                using var clients = JsonDocument.Parse(claim.Value);
                if (clients.RootElement.ValueKind != JsonValueKind.Object) return false;
                if (clients.RootElement.TryGetProperty(coreRoleClientId, out var client) && !ReadRoles(client, roles)) return false;
            }
            if (roles.Count != 1) return false;
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var claim in user.FindAll(PrincipalClaims.Groups))
            {
                // The JWT handler expands a string array into individual claims. A JSON array claim is also supported.
                if (claim.ValueType == "JSON_ARRAY")
                {
                    using var values = JsonDocument.Parse(claim.Value);
                    if (!ReadGroups(values.RootElement, groups)) return false;
                }
                else if (claim.ValueType != ClaimValueTypes.String || string.IsNullOrWhiteSpace(claim.Value)) return false;
                else groups.Add(claim.Value);
            }
            // Keycloak's organization-group mapper emits renameable paths, not the provisioned IDs used by ACLs.
            // Validate that metadata, but only the standard groups claim contributes principal group identifiers.
            if (entries[0].Value.TryGetProperty("groups", out var scopedGroups)
                && !ReadGroups(scopedGroups, new HashSet<string>(StringComparer.Ordinal))) return false;
            var claims = user.Claims.Where(c => c.Type != PrincipalClaims.TenantId && c.Type != PrincipalClaims.LegacyFirmId
                && c.Type != PrincipalClaims.Role && c.Type != ClaimTypes.Role && c.Type != PrincipalClaims.Groups).ToList();
            claims.Add(new Claim(PrincipalClaims.TenantId, tenant.Value));
            claims.Add(new Claim(PrincipalClaims.Role, roles.Single().ToString()));
            claims.AddRange(groups.Order(StringComparer.Ordinal).Select(group => new Claim(PrincipalClaims.Groups, group)));
            normalized = new ClaimsPrincipal(new ClaimsIdentity(claims, user.Identity.AuthenticationType,
                PrincipalClaims.UserId, PrincipalClaims.Role));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool ReadRoles(JsonElement scope, HashSet<Role> roles)
    {
        if (scope.ValueKind != JsonValueKind.Object) return false;
        if (!scope.TryGetProperty("roles", out var values)) return true;
        if (values.ValueKind != JsonValueKind.Array) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String) return false;
            var name = value.GetString();
            if (name == "platform_operator") roles.Add(Role.PLATFORM_ADMIN);
            else if (Enum.GetNames<Role>().Contains(name, StringComparer.Ordinal)) roles.Add(Enum.Parse<Role>(name!));
        }
        return true;
    }

    private static bool ReadGroups(JsonElement values, HashSet<string> groups)
    {
        if (values.ValueKind != JsonValueKind.Array) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) return false;
            groups.Add(value.GetString()!);
        }
        return true;
    }
}
