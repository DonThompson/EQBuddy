using System.Reflection;
using System.Text.Json;

namespace EQBuddy.Core;

public sealed class EpicQuestChecklistCatalog
{
    public List<EpicQuestClassChecklist> Classes { get; set; } = [];

    public static EpicQuestChecklistCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("EQBuddy.Core.Data.EpicQuestChecklist.json");
        if (stream is null) return new EpicQuestChecklistCatalog();
        try
        {
            return JsonSerializer.Deserialize<EpicQuestChecklistCatalog>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new EpicQuestChecklistCatalog();
        }
        catch (Exception ex)
        {
            CoreLog.Error(ex);
            return new EpicQuestChecklistCatalog();
        }
    }
}

public sealed class EpicQuestClassChecklist
{
    public string ClassName { get; set; } = "";
    public List<EpicQuestChecklistRow> Rows { get; set; } = [];
}

public sealed class EpicQuestChecklistRow
{
    public string Id { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Section { get; set; } = "";
    public string Text { get; set; } = "";
    public int Order { get; set; }
    public bool AvailableInClassic { get; set; } = true;
}
