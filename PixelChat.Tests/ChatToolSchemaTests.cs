using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelChat.Art;
using PixelChat.Chat;
using PixelChat.Llm;
using PixelChat.Sprites;

namespace PixelChat.Tests;

public sealed class ChatToolSchemaTests
{
    [Fact]
    public async Task AccountRequestContainsValidTypedSchemasForEntireRegistry()
    {
        var sprites = new SpriteToolRegistry(null!, null!, null!, null!, null!);
        var registry = new AssistantToolRegistry(null!, null!, null!, sprites, null!, null!,
            Options.Create(new AgentOptions()), Options.Create(new PixelChat.Art.ImageGenerationOptions()));
        var tools = registry.Build(Guid.NewGuid(), new(0));
        var request = await CaptureAsync(tools);
        var definitions = request.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(tools.Count, definitions.Length);
        foreach (var tool in definitions)
        {
            var schema = tool.GetProperty("parameters");
            CheckSchema(schema, schema, tool.GetProperty("strict").GetBoolean(), tool.GetProperty("name").GetString()!);
        }
        var apply = definitions.Single(t => t.GetProperty("name").GetString() == "sprite_apply");
        Assert.False(apply.GetProperty("strict").GetBoolean());
        var operation = apply.GetProperty("parameters").GetProperty("properties").GetProperty("operations").GetProperty("items");
        Assert.Equal("object", operation.GetProperty("type").GetString());
        Assert.True(operation.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(operation.GetProperty("required").EnumerateArray(), x => x.GetString() == "op");
        Assert.True(definitions.Single(t => t.GetProperty("name").GetString() == "sprite_read").GetProperty("strict").GetBoolean());
    }

    [Fact]
    public async Task StrictNormalizationKeepsReferencedDefinitions()
    {
        var tool = new ReferenceFunction(AIFunctionFactory.Create((string name) => name, "reference_test"));
        var request = await CaptureAsync([tool]);
        var schema = request.GetProperty("tools")[0].GetProperty("parameters");
        CheckSchema(schema, schema, true, "reference_test");
        Assert.Equal("object", schema.GetProperty("$defs").GetProperty("Target").GetProperty("type").GetString());
    }

    private static async Task<JsonElement> CaptureAsync(IList<AITool> tools)
    {
        using var handler = new CaptureHandler(); using var http = new HttpClient(handler);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_account_id":"fixture"}}"""));
        using var client = new OpenAIAccountChatClient(http, $"test.{payload}.test", "gpt-6-astra", "low", NullLogger<OpenAIAccountChatClient>.Instance);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Schema fixture")], new ChatOptions { Tools = tools });
        return handler.Body;
    }

    private static void CheckSchema(JsonElement schema, JsonElement root, bool strict, string path)
    {
        Assert.True(schema.ValueKind == JsonValueKind.Object, $"{path}: schema must be an object");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = root;
            Assert.StartsWith("#/", reference.GetString());
            foreach (var segment in reference.GetString()![2..].Split('/'))
                Assert.True(target.TryGetProperty(segment.Replace("~1", "/").Replace("~0", "~"), out target), $"{path}: unresolved {reference}");
            return;
        }
        var union = schema.TryGetProperty("anyOf", out var anyOf);
        Assert.True(schema.TryGetProperty("type", out var type) || union, $"{path}: schema must have a type or union");
        if (union) foreach (var child in anyOf.EnumerateArray()) CheckSchema(child, root, strict, path + ".anyOf");
        var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToArray() : [type.ValueKind == JsonValueKind.String ? type.GetString() : null];
        if (types.Contains("array")) CheckSchema(schema.GetProperty("items"), root, strict, path + ".items");
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject()) CheckSchema(property.Value, root, strict, path + "." + property.Name);
            if (strict)
            {
                Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
                Assert.Equal(properties.EnumerateObject().Select(p => p.Name).Order(), schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
            }
        }
        if (schema.TryGetProperty("$defs", out var defs)) foreach (var definition in defs.EnumerateObject()) CheckSchema(definition.Value, root, strict, path + ".$defs." + definition.Name);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
    private sealed class ReferenceFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"target":{"$ref":"#/$defs/Target"}},"$defs":{"Target":{"type":"object","properties":{"name":{"type":"string"}}}}}
            """);
    }
}
