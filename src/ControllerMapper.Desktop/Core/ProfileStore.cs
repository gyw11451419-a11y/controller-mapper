using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ControllerMapper.Desktop.Core;

public sealed class ProfileStore
{
    public const int MinimumTurboIntervalMs = 50;
    public const int MaximumTurboIntervalMs = 1000;
    private const long MaximumJsonBytes = 1_048_576;
    private const int MaximumProfiles = 100;
    private const int MaximumMappings = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public string StoragePath { get; } = Path.Combine(AppPaths.DataDirectory, "profiles.json");

    public ProfileDocument LoadOrCreate()
    {
        if (!File.Exists(StoragePath))
        {
            var profile = new Profile();
            var initial = new ProfileDocument { Profiles = [profile], SelectedProfileId = profile.Id };
            Save(initial);
            return initial;
        }

        var document = ReadDocument(StoragePath, out var migrated);
        if (migrated)
        {
            var backupPath = StoragePath + ".v1.bak";
            if (!File.Exists(backupPath)) File.Copy(StoragePath, backupPath);
            Save(document);
        }
        return document;
    }

    public ProfileDocument ReadDocument(string path) => ReadDocument(path, out _);

    private ProfileDocument ReadDocument(string path, out bool migrated)
    {
        migrated = false;
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaximumJsonBytes)
            throw new InvalidDataException("配置文件不存在或超过 1 MiB 限制。");

        var json = File.ReadAllText(path);
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject
            ?? throw new InvalidDataException("配置文件根节点必须是对象。");
        var versionKey = root.Select(item => item.Key)
            .FirstOrDefault(key => key.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("配置文件缺少 SchemaVersion。");
        var version = root[versionKey]?.GetValue<int>()
            ?? throw new InvalidDataException("SchemaVersion 无效。");
        if (version == 1)
        {
            var profilesKey = root.Select(item => item.Key)
                .FirstOrDefault(key => key.Equals("Profiles", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("配置文件缺少 Profiles。");
            if (root[profilesKey] is not JsonArray profiles)
                throw new InvalidDataException("Profiles 必须是数组。");
            foreach (var item in profiles)
            {
                if (item is not JsonObject profile)
                    throw new InvalidDataException("Profile 必须是对象。");
                var obsoleteKey = profile.Select(field => field.Key)
                    .FirstOrDefault(key => key.Equals("ExecutablePath", StringComparison.OrdinalIgnoreCase));
                if (obsoleteKey is not null) profile.Remove(obsoleteKey);
            }
            root[versionKey] = 2;
            json = root.ToJsonString();
            migrated = true;
        }
        else if (version != 2)
        {
            throw new InvalidDataException("不支持的配置版本。");
        }

        var document = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("配置文件为空。");
        Validate(document);
        return document;
    }

    public void Save(ProfileDocument document)
    {
        Validate(document);
        var directory = Path.GetDirectoryName(StoragePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = StoragePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, StoragePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Profile ImportSingleProfile(string path)
    {
        var document = ReadDocument(path);
        if (document.Profiles.Count != 1)
            throw new InvalidDataException("导入文件必须只包含一个配置。");
        var profile = document.Profiles[0];
        profile.Id = Guid.NewGuid().ToString("N");
        return profile;
    }

    public void ExportSingleProfile(Profile profile, string path)
    {
        var document = new ProfileDocument { Profiles = [profile], SelectedProfileId = profile.Id };
        Validate(document);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public static void Validate(ProfileDocument document)
    {
        if (document.SchemaVersion != 2)
            throw new InvalidDataException("不支持的配置版本。");
        if (document.Profiles is null || document.Profiles.Count is < 1 or > MaximumProfiles)
            throw new InvalidDataException("配置数量无效。");
        if (document.SelectedProfileId is { Length: > 0 } selectedId &&
            !document.Profiles.Any(profile => profile?.Id == selectedId))
            throw new InvalidDataException("上次选择的配置不存在。");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id)
                || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80)
                throw new InvalidDataException("配置 ID 或名称无效。");
            if (profile.TurboIntervalMs is < MinimumTurboIntervalMs or > MaximumTurboIntervalMs)
                throw new InvalidDataException($"连发间隔必须为 {MinimumTurboIntervalMs}–{MaximumTurboIntervalMs} 毫秒。");
            if (!IsSource(profile.ShiftButton, allowNone: true) ||
                !IsSource(profile.TurboTrigger, allowNone: true) ||
                !IsOutput(profile.TurboTarget, allowNone: true) ||
                (profile.TurboTrigger != GamepadButton.None && profile.TurboTarget == GamepadButton.None) ||
                (profile.TurboTrigger == GamepadButton.None && profile.TurboTarget != GamepadButton.None) ||
                (profile.ShiftButton != GamepadButton.None && profile.ShiftButton == profile.TurboTrigger))
                throw new InvalidDataException("Shift 或连发按键配置无效。");

            if (profile.Mappings is null || profile.Mappings.Count > MaximumMappings)
                throw new InvalidDataException("映射数量无效。");
            var sources = new HashSet<(GamepadButton Source, bool Shifted)>();
            foreach (var mapping in profile.Mappings)
            {
                if (mapping is null || !IsSource(mapping.Source) || !IsOutput(mapping.Target) ||
                    (mapping.SecondTarget is { } second && second != GamepadButton.None &&
                     (!IsOutput(second) || second == mapping.Target)) ||
                    !sources.Add((mapping.Source, mapping.Shifted)) ||
                    mapping.Source == profile.TurboTrigger || mapping.Source == profile.ShiftButton ||
                    (profile.TurboTarget != GamepadButton.None &&
                     (mapping.Target == profile.TurboTarget || mapping.SecondTarget == profile.TurboTarget)))
                    throw new InvalidDataException("按键映射存在无效、重复或冲突条目。");
            }
        }
    }

    public static bool IsSource(GamepadButton button, bool allowNone = false) =>
        Enum.IsDefined(button) && (allowNone || button != GamepadButton.None);

    public static bool IsOutput(GamepadButton button, bool allowNone = false) =>
        Enum.IsDefined(button) &&
        (button is not (GamepadButton.LeftPaddle or GamepadButton.RightPaddle)) &&
        (allowNone || button != GamepadButton.None);
}
