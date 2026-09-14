using System.Text.Json;
using Jint;

namespace PixelChat.Sprites;

public sealed record SpriteScriptInput(SpriteSnapshot Snapshot, string Script);
public sealed record SpriteScriptOutput(IReadOnlyList<JsonElement>? Operations, string? Error);

/// <summary>Dedicated process entry point. No host objects, CLR access, modules, I/O, or credentials are exposed to JavaScript.</summary>
public static class SpriteScriptWorker
{
    public const int MaxMessageChars = 4_000_000;
    public static async Task RunAsync()
    {
        try
        {
            var input = await ReadBoundedAsync(Console.In, MaxMessageChars, CancellationToken.None);
            var request = JsonSerializer.Deserialize<SpriteScriptInput>(input, SpriteDocument.JsonOptions) ?? throw new InvalidOperationException("Missing script request.");
            var result = Execute(request);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(new SpriteScriptOutput(result, null), SpriteDocument.JsonOptions));
        }
        catch (Exception ex)
        {
            await Console.Out.WriteAsync(JsonSerializer.Serialize(new SpriteScriptOutput(null, ex.Message), SpriteDocument.JsonOptions));
            Environment.ExitCode = 1;
        }
    }

    public static IReadOnlyList<JsonElement> Execute(SpriteScriptInput request)
    {
        if (request.Script.Length > 262_144) throw new InvalidOperationException("Script exceeds 256K characters.");
        using var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(2))
            .MaxStatements(250_000)
            .LimitRecursion(64)
            .LimitMemory(32_000_000));
        // The API and snapshot are pure JS values, avoiding CLR delegate/object wrappers entirely.
        var snapshot = JsonSerializer.Serialize(request.Snapshot, SpriteDocument.JsonOptions);
        var program = """
            (function() {
                'use strict';
                const commands = [];
                const api = Object.freeze({
                    apply(op) {
                        if (commands.length >= 10000) throw new Error('Command limit exceeded');
                        commands.push(JSON.parse(JSON.stringify(op)));
                    },
                    batch(ops) { for (const op of ops) this.apply(op); }
                });
                const snapshot =
            """ + snapshot + ";\n(function(sprite, document, revision) { 'use strict';\n" + request.Script + "\n})(api, snapshot.document, snapshot.revision);\nreturn JSON.stringify(commands);\n})()";
        var output = engine.Evaluate(program).AsString();
        if (output.Length > MaxMessageChars) throw new InvalidOperationException("Script output budget exceeded.");
        var commands = JsonSerializer.Deserialize<List<JsonElement>>(output, SpriteDocument.JsonOptions) ?? [];
        if (commands.Count is < 1 or > 10000) throw new InvalidOperationException("Script must emit 1–10000 commands.");
        return commands;
    }

    public static async Task<string> ReadBoundedAsync(TextReader reader, int limit, CancellationToken cancellationToken)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) return result.ToString();
            if (result.Length + count > limit) throw new InvalidOperationException("Worker message budget exceeded.");
            result.Append(buffer, 0, count);
        }
    }
}
