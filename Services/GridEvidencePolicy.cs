using JtdxAutoResume.V3.Models;

namespace JtdxAutoResume.V3.Services;

public static class GridEvidencePolicy
{
    public static bool TryGetDirectGrid(DecodeMessage decode, out string grid)
    {
        grid = "";
        var normalized = MaidenheadGrid.Normalize(decode.TransmittedGrid);
        if (!normalized.IsValid)
            return false;

        var targetCall = CallsignNormalizer.Normalize(
            string.IsNullOrWhiteSpace(decode.ContactableCall) ? decode.Callsign : decode.ContactableCall);
        var gridOwner = CallsignNormalizer.Normalize(decode.GridOwnerCall);
        if (!string.IsNullOrWhiteSpace(gridOwner)
            && !gridOwner.Equals(targetCall, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        grid = string.IsNullOrWhiteSpace(normalized.Grid6) ? normalized.Grid4 : normalized.Grid6;
        return true;
    }

    public static string MostRecentDirectGrid(IEnumerable<DecodeMessage> decodes, string callsign)
    {
        var targetCall = CallsignNormalizer.Normalize(callsign);
        if (string.IsNullOrWhiteSpace(targetCall))
            return "";

        foreach (var decode in decodes.OrderByDescending(item => item.ReceivedAt))
        {
            var decodeCall = CallsignNormalizer.Normalize(
                string.IsNullOrWhiteSpace(decode.ContactableCall) ? decode.Callsign : decode.ContactableCall);
            if (!decodeCall.Equals(targetCall, StringComparison.OrdinalIgnoreCase))
                continue;
            if (TryGetDirectGrid(decode, out var grid))
                return grid;
        }

        return "";
    }

    public static bool IsConflictingQrzWantedGrid(WantedItem item, DecodeMessage directDecode)
    {
        if (!item.GridSource.Equals("QRZ", StringComparison.OrdinalIgnoreCase)
            || !TryGetDirectGrid(directDecode, out var directGrid))
        {
            return false;
        }

        var itemCall = CallsignNormalizer.Normalize(
            string.IsNullOrWhiteSpace(item.ContactableCall) ? item.Call : item.ContactableCall);
        var decodeCall = CallsignNormalizer.Normalize(
            string.IsNullOrWhiteSpace(directDecode.ContactableCall) ? directDecode.Callsign : directDecode.ContactableCall);
        if (string.IsNullOrWhiteSpace(itemCall)
            || !itemCall.Equals(decodeCall, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var itemGrid = MaidenheadGrid.Normalize(FirstNonBlank(item.NormalizedGrid4, item.WantedValue, item.Grid));
        var transmitted = MaidenheadGrid.Normalize(directGrid);
        return itemGrid.IsValid
            && transmitted.IsValid
            && !itemGrid.Grid4.Equals(transmitted.Grid4, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonBlank(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}
