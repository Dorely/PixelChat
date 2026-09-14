using PixelChat.Sprites;

namespace PixelChat.Tests;

public sealed class SpriteScriptTests
{
    private static SpriteScriptInput Input(string source) => new(new(Guid.NewGuid(), 0, new() { Layers = [new()], Frames = [new()] }), source);
    [Fact]
    public async Task WorkerReturnsOneBatchWithoutHostObjects()
    {
        var commands = await SpriteScriptService.EvaluateInWorkerAsync(Input("for(let i=0;i<4;i++) sprite.apply({op:'addLayer',name:'layer '+i});"));
        Assert.Equal(4, commands.Count);
        Assert.Equal("layer 3", commands[3].GetProperty("name").GetString());
    }
    [Theory]
    [InlineData("while(true) {}")]
    [InlineData("function recurse(){recurse()} recurse();")]
    [InlineData("require('fs').readFileSync('secret');")]
    [InlineData("System.IO.File.ReadAllText('secret');")]
    [InlineData("fetch('https://example.com');")]
    [InlineData("let x=[]; while(true) x.push(new Array(10000).fill('allocation')); ")]
    public async Task WorkerRejectsRunawayAndForbiddenAccess(string script)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => SpriteScriptService.EvaluateInWorkerAsync(Input(script)));
    }
    [Fact]
    public async Task ParentCancellationTerminatesWorker()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SpriteScriptService.EvaluateInWorkerAsync(Input("while(true){}"), cancellation.Token));
    }
}
