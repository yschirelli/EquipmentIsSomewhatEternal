using System.Collections.Concurrent;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EquipmentIsEternal;

public static class EquipmentSnapshot
{
    private static readonly ConcurrentDictionary<string, SnapshotData> Snapshots = new();

    public record SnapshotData
    {
        public List<Item> EquipmentItems { get; init; } = [];
        public List<Item> QuestItems { get; init; } = [];
        public List<string> InsuredItemIds { get; init; } = [];
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

    public static List<Item> DeepCloneItems(IEnumerable<Item> items)
    {
        var json = JsonSerializer.Serialize(items);
        return JsonSerializer.Deserialize<List<Item>>(json) ?? [];
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
