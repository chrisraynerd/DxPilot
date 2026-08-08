using System.IO;
using System.Text.Json;
using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public sealed class SessionHistoryArchiveStore
{
    private sealed class ArchiveDocument
    {
        public int Version { get; set; } = 1;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public List<SessionDxOpportunity> Entries { get; set; } = new();
    }

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        IgnoreReadOnlyProperties = true
    };

    public SessionHistoryArchiveStore(string appFolder)
    {
        ArchiveFile = Path.Combine(appFolder, "session_history_archive.json");
    }

    public string ArchiveFile { get; }

    public IReadOnlyList<SessionDxOpportunity> Load(out string warning)
    {
        warning = "";
        try
        {
            if (!File.Exists(ArchiveFile))
                return Array.Empty<SessionDxOpportunity>();

            var document = JsonSerializer.Deserialize<ArchiveDocument>(File.ReadAllText(ArchiveFile), _jsonOptions);
            return document?.Entries ?? new List<SessionDxOpportunity>();
        }
        catch (Exception ex)
        {
            warning = $"Full Archive could not be loaded: {ex.GetBaseException().Message}";
            return Array.Empty<SessionDxOpportunity>();
        }
    }

    public bool Save(IEnumerable<SessionDxOpportunity> entries, out string error)
    {
        error = "";
        try
        {
            var directory = Path.GetDirectoryName(ArchiveFile);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var document = new ArchiveDocument
            {
                UpdatedUtc = DateTime.UtcNow,
                Entries = entries
                    .OrderBy(entry => entry.SessionStartedUtc)
                    .ThenBy(entry => entry.FirstSeenUtc)
                    .Select(entry => entry.Snapshot())
                    .ToList()
            };
            var temporary = ArchiveFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, _jsonOptions));
            File.Move(temporary, ArchiveFile, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }
}
