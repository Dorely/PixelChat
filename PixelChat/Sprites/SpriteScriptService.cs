using System.Diagnostics;
using System.Text.Json;

namespace PixelChat.Sprites;

public sealed class SpriteScriptService(ISpriteDocumentService documents)
{
    public async Task<SpriteCommit> RunAsync(Guid projectId, Guid documentId, long expectedRevision, string script, string label, string? taskId, CancellationToken cancellationToken)
    {
        var snapshot = await documents.ReadAsync(projectId, documentId, cancellationToken: cancellationToken);
        if (snapshot.Revision != expectedRevision) throw new SpriteConflictException(expectedRevision, snapshot.Revision);
        var commands = await EvaluateInWorkerAsync(new(snapshot, script), cancellationToken);
        return await documents.ApplyAsync(projectId, new(documentId, expectedRevision, label, commands, "agent", taskId, script), cancellationToken);
    }

    public static async Task<IReadOnlyList<JsonElement>> EvaluateInWorkerAsync(SpriteScriptInput input, CancellationToken cancellationToken = default)
    {
        if (input.Script.Length > 262_144) throw new InvalidOperationException("Script exceeds 256K characters.");
        var json = JsonSerializer.Serialize(input, SpriteDocument.JsonOptions);
        if (json.Length > SpriteScriptWorker.MaxMessageChars) throw new InvalidOperationException("Script snapshot budget exceeded.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(SpriteScriptService).Assembly.Location);
        start.ArgumentList.Add("--sprite-script-worker");
        // Child receives only runtime bootstrap environment, never app configuration or provider credentials.
        var environment = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "DOTNET_ROOT" }
            .Select(key => (key, value: Environment.GetEnvironmentVariable(key))).Where(p => p.value is not null).ToList();
        start.Environment.Clear(); foreach (var (key, value) in environment) start.Environment[key] = value;
        start.Environment["DOTNET_GCHeapHardLimit"] = "8000000";
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        if (!process.Start()) throw new InvalidOperationException("Could not start sprite script worker.");
        try
        {
            var stdout = SpriteScriptWorker.ReadBoundedAsync(process.StandardOutput, SpriteScriptWorker.MaxMessageChars, deadline.Token);
            var stderr = SpriteScriptWorker.ReadBoundedAsync(process.StandardError, 65536, deadline.Token);
            await process.StandardInput.WriteAsync(json.AsMemory(), deadline.Token); process.StandardInput.Close();
            while (!process.HasExited)
            {
                deadline.Token.ThrowIfCancellationRequested();
                process.Refresh();
                if (process.WorkingSet64 > 256_000_000) throw new InvalidOperationException("Script worker memory budget exceeded.");
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                await Task.WhenAny(process.WaitForExitAsync(deadline.Token), Task.Delay(100, deadline.Token));
            }
            var result = JsonSerializer.Deserialize<SpriteScriptOutput>(await stdout, SpriteDocument.JsonOptions);
            var errorOutput = await stderr;
            if (process.ExitCode != 0 || result?.Operations is null) throw new InvalidOperationException(result?.Error ?? $"Sprite script worker failed (exit {process.ExitCode}): {errorOutput}");
            if (result.Operations.Count is < 1 or > 10000) throw new InvalidOperationException("Invalid worker command count.");
            return result.Operations;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Sprite script exceeded its execution deadline; no changes were committed.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
