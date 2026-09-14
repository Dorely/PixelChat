using System.Text.Json;
using System.Text.Json.Serialization;
using PixelChat.Models;

namespace PixelChat.Art;

public static class GenerationQueueState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public static GenerationOutputStateView Read(GenerationOutput row) =>
        (JsonSerializer.Deserialize<GenerationOutputStateView>(row.StateJson, JsonOptions)
            ?? new(row.OutputIndex, row.Status)) with { OutputIndex = row.OutputIndex, Status = row.Status };

    public static GenerationOutputErrorView? Error(GenerationOutput row) =>
        row.ErrorJson is null ? null : JsonSerializer.Deserialize<GenerationOutputErrorView>(row.ErrorJson, JsonOptions);

    public static void Apply(GenerationOutput row, GenerationOutputStateView state)
    {
        row.Status = state.Status;
        row.StateJson = JsonSerializer.Serialize(state, JsonOptions);
        if (state.Status is GenerationOutputStatus.Succeeded or GenerationOutputStatus.Queued or GenerationOutputStatus.Deleted)
            row.ErrorJson = null;
    }

    public static void Fail(GenerationOutput row, GenerationOutputErrorView error)
    {
        var previous = Read(row);
        Apply(row, previous with { Status = GenerationOutputStatus.Failed, Message = "Image request failed.",
            Error = error.Error, ErrorKind = error.ErrorKind, RequestId = error.RequestId,
            ResponseId = error.ResponseId, CallId = error.CallId, LastEventType = error.LastEventType,
            EventCount = error.EventCount, UpdatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        row.ErrorJson = JsonSerializer.Serialize(error, JsonOptions);
    }

    public static List<GenerationPromptSpec> Specs(IEnumerable<GenerationPrompt> rows) =>
        rows.OrderBy(row => row.Index).Select(row => new GenerationPromptSpec(row.Prompt, row.Count, row.OutputName)).ToList();

    public static void Initialize(GenerationBatch batch, IReadOnlyList<GenerationPromptSpec> specs, IEnumerable<ArtAsset> references)
    {
        var outputIndex = 0;
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            batch.Prompts.Add(new() { BatchId = batch.Id, Index = index, Prompt = spec.Prompt, Count = spec.Count, OutputName = spec.OutputName });
            for (var within = 0; within < spec.Count; within++)
            {
                var row = new GenerationOutput { BatchId = batch.Id, OutputIndex = outputIndex++, PromptIndex = index, OutputWithinPrompt = within };
                Apply(row, new(row.OutputIndex, GenerationOutputStatus.Queued, Message: "Waiting for an available worker."));
                batch.Outputs.Add(row);
            }
        }
        batch.References = references.Select((asset,index) => new GenerationReference
        {
            BatchId = batch.Id, Index = index, SourceAssetId = asset.Id, Label = asset.Label,
            FileName = asset.FileName, ContentType = asset.ContentType, Data = asset.Data.ToArray(),
        }).ToList();
    }
}
