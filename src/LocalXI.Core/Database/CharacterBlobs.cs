namespace LocalXI.Core.Database;

public enum QuestState
{
    NotStarted,
    Accepted,
    Completed,
}

/// <summary>
/// The chars.quests blob: LandSandBoat's questlog_t[11] copied raw, each area 32 bytes of "current" flags then
/// 32 bytes of "complete" flags, one bit per quest id (byte id / 8, bit id % 8), as read in charutils.cpp.
/// </summary>
public static class QuestLogBlob
{
    public const int Areas = 11;
    public const int QuestsPerArea = 256;
    private const int FlagBytes = QuestsPerArea / 8;
    private const int AreaBytes = FlagBytes * 2;

    public static QuestState Get(byte[]? blob, int area, int quest)
    {
        Check(area, quest);
        var (currentByte, completeByte, mask) = Locate(area, quest);
        if (blob is not null && completeByte < blob.Length && (blob[completeByte] & mask) != 0)
        {
            return QuestState.Completed;
        }

        return blob is not null && currentByte < blob.Length && (blob[currentByte] & mask) != 0 ? QuestState.Accepted : QuestState.NotStarted;
    }

    /// <summary>Returns a copy of the blob (grown to full size if needed) with the quest in the given state.</summary>
    public static byte[] Set(byte[]? blob, int area, int quest, QuestState state)
    {
        Check(area, quest);
        var result = new byte[Math.Max(blob?.Length ?? 0, Areas * AreaBytes)];
        blob?.CopyTo(result, 0);

        var (currentByte, completeByte, mask) = Locate(area, quest);
        result[currentByte] = (byte)(state == QuestState.Accepted ? result[currentByte] | mask : result[currentByte] & ~mask);
        result[completeByte] = (byte)(state == QuestState.Completed ? result[completeByte] | mask : result[completeByte] & ~mask);
        return result;
    }

    private static (int Current, int Complete, int Mask) Locate(int area, int quest)
    {
        var current = area * AreaBytes + quest / 8;
        return (current, current + FlagBytes, 1 << (quest % 8));
    }

    private static void Check(int area, int quest)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(area);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(area, Areas);
        ArgumentOutOfRangeException.ThrowIfNegative(quest);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(quest, QuestsPerArea);
    }
}

/// <summary>
/// The char_unlocks.homepoints blob: LandSandBoat's telepoint_t (uint32 access[4], then int32 menu[10] favourites).
/// Crystal n is bit n % 32 of access[n / 32], as homepoint.lua registers it; in little-endian bytes that is
/// byte n / 8, bit n % 8. The favourites after the access words are kept untouched.
/// </summary>
public static class HomePointBlob
{
    public const int Size = 4 * 4 + 10 * 4;
    public const int MaxCrystals = 128;

    public static bool IsRegistered(byte[]? blob, int index)
    {
        Check(index);
        return blob is not null && index / 8 < blob.Length && (blob[index / 8] & (1 << (index % 8))) != 0;
    }

    public static byte[] Set(byte[]? blob, IEnumerable<int> indices, bool registered)
    {
        var result = new byte[Math.Max(blob?.Length ?? 0, Size)];
        blob?.CopyTo(result, 0);
        foreach (var index in indices)
        {
            Check(index);
            var mask = 1 << (index % 8);
            result[index / 8] = (byte)(registered ? result[index / 8] | mask : result[index / 8] & ~mask);
        }

        return result;
    }

    private static void Check(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxCrystals);
    }
}

/// <summary>
/// chars.keyitems: keyitems_t from LandSandBoat's common/mmo.h, 8 tables of 512 key items, each table a keyList
/// bitset (owned) followed by a seenList bitset (already looked at), 64 bytes each. Key item n is bit n % 512 of
/// table n / 512, in xi::bitset order (byte i / 8, bit i % 8).
/// </summary>
public static class KeyItemBlob
{
    private const int Tables = 8;
    private const int PerTable = 512;
    private const int ListBytes = PerTable / 8;
    public const int Size = Tables * 2 * ListBytes;

    public static bool Has(byte[]? blob, int keyItem)
    {
        var (index, mask) = Locate(keyItem, seen: false);
        return blob is not null && index < blob.Length && (blob[index] & mask) != 0;
    }

    /// <summary>Gives or takes the key items. Taken ones are also marked unseen, as the game does.</summary>
    public static byte[] Set(byte[]? blob, IEnumerable<int> keyItems, bool owned)
    {
        var result = new byte[Math.Max(blob?.Length ?? 0, Size)];
        blob?.CopyTo(result, 0);
        foreach (var keyItem in keyItems)
        {
            var (index, mask) = Locate(keyItem, seen: false);
            result[index] = (byte)(owned ? result[index] | mask : result[index] & ~mask);
            if (!owned)
            {
                var (seenIndex, seenMask) = Locate(keyItem, seen: true);
                result[seenIndex] = (byte)(result[seenIndex] & ~seenMask);
            }
        }

        return result;
    }

    private static (int Index, int Mask) Locate(int keyItem, bool seen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keyItem);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(keyItem, Tables * PerTable);
        var bit = keyItem % PerTable;
        return ((keyItem / PerTable * 2 + (seen ? 1 : 0)) * ListBytes + bit / 8, 1 << (bit % 8));
    }
}
