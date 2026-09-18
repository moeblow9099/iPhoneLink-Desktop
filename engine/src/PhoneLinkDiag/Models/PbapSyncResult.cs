namespace PhoneLinkDiag.Models;

public sealed record PbapSyncResult(
    IReadOnlyList<ContactRecord> Contacts,
    IReadOnlyList<CallHistoryRecord> Calls);
