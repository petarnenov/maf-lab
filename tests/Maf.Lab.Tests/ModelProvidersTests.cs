using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

public class ModelProvidersTests
{
    [Fact]
    public void Remote_chat_endpoint_without_the_env_key_fails_naming_the_variable()
    {
        var options = new ModelOptions { ChatEndpoint = "https://ollama.com", ChatApiKeyEnvironmentVariable = "MAF_TEST_KEY_THAT_IS_NOT_SET" };
        var ex = Assert.Throws<InvalidOperationException>(() => new ModelProviders(Options.Create(options)).CreateChatClient());
        Assert.Contains("MAF_TEST_KEY_THAT_IS_NOT_SET", ex.Message);
    }

    [Fact]
    public void Key_is_read_from_the_environment_at_client_creation()
    {
        const string variable = "MAF_TEST_OLLAMA_KEY";
        Environment.SetEnvironmentVariable(variable, "test-secret-value");
        try
        {
            var options = new ModelOptions { ChatEndpoint = "https://ollama.com", ChatApiKeyEnvironmentVariable = variable };
            Assert.NotNull(new ModelProviders(Options.Create(options)).CreateChatClient());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Local_chat_endpoint_needs_no_key()
    {
        var options = new ModelOptions { ChatEndpoint = "http://localhost:11434", ChatApiKeyEnvironmentVariable = "MAF_TEST_KEY_THAT_IS_NOT_SET" };
        Assert.NotNull(new ModelProviders(Options.Create(options)).CreateChatClient());
    }
}
