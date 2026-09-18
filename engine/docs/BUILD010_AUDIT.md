# iPhoneLink CRM Build 010 Audit

## Scope

The Build 010 source tree was scanned from the first line of every C# source file after the MAP250, multi-phone ownership, optional-contact, and name-resolution changes.

- C# files scanned: **31**
- C# source lines scanned: **8,326**
- Consecutive static audit passes: **2**
- Static audit failures: **0**
- File manifests across the two passes: **identical**
- C# syntax parsing: **no parser ERROR or missing nodes**
- Duplicate MainForm fields/method signatures: **none found**
- Targeted `historyList` declaration-order regression check: **passed**
- Duplicate filenames: **none**
- `bin`, `obj`, `.vs`, compiled DLL/EXE/PDB remnants: **none**
- Top-level BAT/CMD launchers: **none**
- PowerShell scripts/executable PowerShell commands: **none**

## MAP and multi-phone checks

- Default UI message extraction limit is **250**.
- MAP listing default is **250**.
- Bridge `/messages` and `/refresh-inbox` defaults are **250**.
- Inbox and Sent handles are merged to a maximum of 250 reads per synchronization.
- Message records remain keyed by `DeviceId + Folder + Handle`.
- Each explicit phone sync requires the exact requested `DeviceId`; it cannot silently fall back to another phone.
- A live MAP session belonging to a different phone is disconnected before the selected phone is used.
- Newly connected phones are auto-attempted sequentially for messages and call history.
- Selecting a phone always retries that phone independently of the one-time connected-phone background marker.
- Disconnect/reconnect clears the background auto-sync marker for that phone.

The uploaded `READ-INBOX-FAST-report-20260720-042013.zip` was inspected separately. For the tested iPhone session it reports MAP/PBAP/HFP advertised, RFCOMM and OBEX connected, 10 handles found, 10 messages read, 10 decoded bodies, and no error. This validates successful GetMessage/body decoding for that tested session; it does not prove that every phone exposes 250 messages.

## Contacts and call-history checks

- Call-history synchronization runs automatically without requiring the app's Sync Contacts toggle.
- Sync Contacts defaults off and is used only for optional address-book import/name enrichment.
- PBAP requests `FN`, structured `N`, `TEL`, `EMAIL`, `ORG`, and call-datetime fields.
- Phone-number-like `FN/N` values are rejected as display names.
- Contact names are matched back onto call records by normalized phone number when available.
- Existing call-history rows are re-enriched after a later contact sync.
- Contacts and call records remain associated with their source `DeviceId`.

## Locked HFP invariant

The outbound HFP dial command remains `ATD{sanitized};`. No `ATE0` send path was added.

## Installer note

The package is editable source. `INSTALL-iPhoneLinkCRM.vbs` runs the source publish command hidden and does not invoke PowerShell. It still requires a local .NET 8 SDK because this environment cannot produce or validate the final Windows self-contained binary.

## Validation limitation

This environment does not contain the .NET 8 Windows SDK or a Windows Bluetooth stack. The package is therefore not claimed as compiler-verified or physical two-phone hardware-verified here. Source syntax and structural checks passed twice; final Windows compilation and real-device testing remain required on the Windows test PC.
