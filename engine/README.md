# iPhoneLink CRM Premium — Build 010 Source

This package is the clean editable source for the current iPhoneLink desktop dialer/CRM branch. It contains one authoritative C# source tree and no patched compiled DLLs, `bin`, `obj`, `.vs`, or stale published application directory.

## Locked HFP call path

The proven outbound HFP dial command is unchanged:

```text
ATD{sanitized};
```

The validated HFP initialization path remains unchanged in this branch. Raw AT/HFP/RFCOMM setup traffic stays in Developer Diagnostics. User-facing Notifications are reserved for clean call events and explicit call-state failures.

## Automatic message and call-history extraction

- Connected mobile phones are detected and synchronized automatically.
- MAP message history defaults to **up to 250 messages total** across Inbox and Sent.
- Message listing uses paging where the phone supports it.
- Individual failed message reads retry once without aborting the remaining history.
- MAP session ownership is tied to the target `DeviceId`; switching phones cannot intentionally reuse another phone's live MAP/OBEX session.
- A manual phone selection always retries that selected phone, even when it was auto-synchronized earlier.
- Newly connected phones are auto-synchronized once per connection; disconnect/reconnect makes the phone eligible for automatic synchronization again.
- Message data remains keyed by `DeviceId + Folder + Handle`, so phone 1 and phone 2 do not overwrite one another.
- The visible Messages view follows the currently selected phone. If no phone is selected, cached records from all phones can be shown.
- Call history is pulled automatically through PBAP when the connected phone exposes the call-history repositories.

The app does not present a message-permission prompt before attempting MAP synchronization. It simply attempts the profiles exposed by the connected phone and keeps raw failures in Developer Diagnostics.

## Optional contact synchronization and names

`Sync Contacts` is **off by default**.

- Messages and call history do not require the app's Sync Contacts toggle.
- Turning on `Sync Contacts` imports the address book from currently connected PBAP-capable phones.
- Contact parsing requests and reads `FN`, structured `N`, `TEL`, `EMAIL`, and `ORG` fields.
- Phone-number-like `FN/N` values are no longer treated as real contact names.
- When a real contact name is available, call-history rows for the same phone are immediately enriched by normalized phone-number matching.
- Existing call-history rows are renamed after a later contact sync; the user does not need to download call history a second time just to populate names.
- Contacts and call records remain associated with their originating `DeviceId`.
- Contacts and Calls views follow the selected phone while preserving other phones' data in memory.

The operating system and phone ultimately determine which Bluetooth profiles and fields are exposed. If a phone does not expose contact names, the app cannot manufacture them; enabling the phone's own contact-sync sharing is only needed when the user chooses to import contact names/address-book data.

## Multi-phone behavior

- Multiple paired/connected phones are kept as distinct devices.
- On startup and paired-device refresh, every currently connected recognized mobile phone is auto-attempted sequentially for messages and call history.
- Selecting phone 2 forces device-specific MAP session ownership and retries phone 2 independently of phone 1.
- Disconnecting one phone does not delete another phone's cached message history.
- A disconnected phone is removed from the connected auto-sync set; reconnecting it triggers a fresh automatic attempt.
- PBAP operations open a fresh per-device read-only session instead of sharing MAP state.
- HFP calling remains independently device-selected and unchanged.

## Local CRM bridge

Bridge URL:

```text
http://127.0.0.1:8765/
```

`/health` is public. Protected routes require the per-install token.

Default message endpoints now use 250 when no limit is supplied:

```text
GET /messages?limit=250
GET /refresh-inbox?limit=250
```

Token file:

```text
%LOCALAPPDATA%\iPhoneLinkCRM\bridge-token.txt
```

## Install/run note

This ZIP is an editable source package. `INSTALL-iPhoneLinkCRM.vbs` is the single top-level install entry point. It runs the source publish flow hidden, creates the desktop shortcut, and launches the app without displaying a PowerShell or Command Prompt window. The .NET 8 SDK is still required because this package is not precompiled.

`RUN-iPhoneLinkCRM.vbs` launches only the installed executable and does not silently rebuild from source.

See `docs/PROOF_CHECKLIST.md` and `docs/BUILD010_AUDIT.md`.
