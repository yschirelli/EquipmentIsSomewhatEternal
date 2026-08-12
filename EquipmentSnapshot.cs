using System.Collections.Concurrent;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

namespace EquipmentIsEternal;

public static class EquipmentSnapshot
{
    private static readonly ConcurrentDictionary<string, SnapshotData> Snapshots = new();
    private static readonly ConcurrentDictionary<string, PendingRestore> PendingRestores = new();

    private static string? _backupDir;

    public record SnapshotData
    {
        public List<Item> EquipmentItems { get; init; } = [];
        public List<string> InsuredItemIds { get; init; } = [];
    }

    public record PendingRestore
    {
        public required SnapshotData Snapshot { get; init; }
        public List<Item>? QuestItems { get; init; }
    }

    public static void SetBackupDir(string dir)
    {
        _backupDir = dir;
        Directory.CreateDirectory(dir);
    }

    public static void Store(string sessionId, SnapshotData data)
    {
        Snapshots[sessionId] = data;
    }

    public static SnapshotData? Retrieve(string sessionId)
    {
        Snapshots.TryRemove(sessionId, out var data);
        return data;
    }

    public static void MarkPendingRestore(string sessionId, PendingRestore pending)
    {
        PendingRestores[sessionId] = pending;
    }

    public static PendingRestore? ConsumePendingRestore(string sessionId)
    {
        PendingRestores.TryRemove(sessionId, out var data);
        return data;
    }

    public static string? WriteBackup(string sessionId, SnapshotData data)
    {
        if (_backupDir == null) return null;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = System.IO.Path.Combine(_backupDir, $"{sessionId}_{timestamp}_snapshot.json");
        var json = JsonSerializer.Serialize(data, JsonUtil.JsonSerializerOptionsIndented);
        File.WriteAllText(path, json);
        return path;
    }

    public static string? WriteProfileBackup(string sessionId, object profile)
    {
        if (_backupDir == null) return null;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = System.IO.Path.Combine(_backupDir, $"{sessionId}_{timestamp}_profile.json");
        var json = JsonSerializer.Serialize(profile, JsonUtil.JsonSerializerOptionsIndented);
        File.WriteAllText(path, json);
        return path;
    }

    public static List<Item> DeepCloneItems(IEnumerable<Item> items)
    {
        var opts = JsonUtil.JsonSerializerOptionsNoIndent;
        var json = JsonSerializer.Serialize(items, opts);
        return JsonSerializer.Deserialize<List<Item>>(json, opts) ?? [];
    }

    public static List<Item> GetItemAndChildren(List<Item> allItems, string parentId, string slotId)
    {
        var result = new List<Item>();
        var root = allItems.FirstOrDefault(i => i.ParentId == parentId && i.SlotId == slotId);
        if (root == null) return result;

        result.Add(root);
        CollectChildren(allItems, root.Id!, result);
        return result;
    }

    public static List<Item> GetItemAndChildrenByPrefix(List<Item> allItems, string parentId, string slotPrefix)
    {
        var result = new List<Item>();
        foreach (var root in allItems.Where(i => i.ParentId == parentId && i.SlotId != null && i.SlotId.StartsWith(slotPrefix)))
        {
            result.Add(root);
            CollectChildren(allItems, root.Id!, result);
        }
        return result;
    }

    public static List<Item> GetAllChildrenOf(List<Item> allItems, string containerId)
    {
        var result = new List<Item>();
        foreach (var item in allItems.Where(i => i.ParentId == containerId))
        {
            result.Add(item);
            CollectChildren(allItems, item.Id!, result);
        }
        return result;
    }

    public static void CollectChildrenPublic(List<Item> allItems, string parentId, List<Item> result)
        => CollectChildren(allItems, parentId, result);

    private static void CollectChildren(List<Item> allItems, string parentId, List<Item> result)
    {
        foreach (var child in allItems.Where(i => i.ParentId == parentId))
        {
            result.Add(child);
            CollectChildren(allItems, child.Id!, result);
        }
    }
}
