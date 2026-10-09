using System.ComponentModel;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;

// The sample MCP server (introduce-plugins 6.1): the caller's bearer token is the platform's own (Auth:Issuer,
// Auth:Audience, Auth:SigningKey from platform.env), validated with stock JwtBearer; the tenant comes from its claim only.
var builder = WebApplication.CreateBuilder(args);
var auth = builder.Configuration.GetSection("Auth");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = auth["Issuer"] ?? "maf-lab-dev-issuer",
        ValidAudience = "_example",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth["SigningKey"]
            ?? throw new InvalidOperationException("Auth:SigningKey is required."))),
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "maf-lab-example", Version = "1.0.0" })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<ExampleTools>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapMcp("/mcp").RequireAuthorization();
app.Run();

/// <summary>What the model gets back: a small record made for it, never a stored entity.</summary>
public sealed record ExampleFact(string Tenant, string Query, string Fact);

[McpServerToolType]
public sealed class ExampleTools(IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_example_fact", ReadOnly = true)]
    [Description("The sample fact for the caller's tenant, for the question asked.")]
    public ExampleFact GetExampleFact([Description("The question, as the person asked it.")] string query)
    {
        // The tenant is the token's, never a parameter; the query is never logged.
        var tenant = http.HttpContext?.User.FindFirst("tenant_id")?.Value ?? throw new UnauthorizedAccessException();
        return new ExampleFact(tenant, query, $"Every tenant has its own sample fact; this one belongs to {tenant}.");
    }
}
