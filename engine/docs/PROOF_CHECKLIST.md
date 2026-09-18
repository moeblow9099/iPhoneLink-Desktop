# iPhoneLink Windows hardware proof checklist — Build 010

## Multi-phone automatic synchronization

- [ ] Connect phone 1 and phone 2.
- [ ] Launch the app without manually starting message sync.
- [ ] Confirm both connected phones are independently attempted for MAP messages and PBAP call history.
- [ ] Select phone 1 and confirm its Messages, Contacts, and Calls views show phone 1 data.
- [ ] Select phone 2 and confirm the views switch to phone 2 data.
- [ ] Confirm selecting phone 2 retries phone 2 even if phone 1 was synchronized first.
- [ ] Disconnect phone 2; confirm phone 1 data remains intact.
- [ ] Reconnect phone 2; confirm it becomes eligible for a fresh automatic synchronization.

## MAP message history

- [ ] Confirm the default message limit is 250.
- [ ] Confirm Inbox messages are read when exposed.
- [ ] Confirm Sent messages are read when exposed.
- [ ] Confirm up to 250 combined Inbox/Sent messages are retained for the selected synchronization.
- [ ] Confirm an individual GetMessage failure retries once and does not abort remaining handles.
- [ ] Confirm phone 1 and phone 2 records use separate DeviceId keys.
- [ ] Confirm the app does not show a message-permission prompt before attempting MAP.
- [ ] Confirm raw MAP/OBEX errors remain in Developer Diagnostics.

## PBAP call history and optional contacts

- [ ] Leave Sync Contacts off and confirm call-history synchronization still runs automatically where the phone exposes it.
- [ ] Confirm call history can show phone-provided names when present.
- [ ] Turn Sync Contacts on only when contact import is desired.
- [ ] Confirm real FN/N contact names are shown instead of repeating the phone number as the Name value.
- [ ] Confirm contacts from each connected phone remain associated with that phone.
- [ ] Confirm contact sync immediately enriches already-loaded call-history names by matching normalized phone numbers.
- [ ] Confirm switching phones changes the visible Contacts and Calls data without deleting the other phone's records.

## Locked HFP calling

- [ ] Place a call from phone 1.
- [ ] Place a call from phone 2.
- [ ] Confirm `+1` formatting remains correct.
- [ ] Confirm exactly one clean Call Started and Call Ended event per call.
- [ ] Confirm `+CIND`, `+CIEV`, AT echoes, RFCOMM/HFP setup traffic, and generic probe ERROR text remain in Developer Diagnostics.
- [ ] Confirm explicit BUSY, NO ANSWER, NO CARRIER, and call-time CME/CMS failures can still surface as genuine failures.

## Packaging

- [ ] Source build compiles successfully with .NET 8 on Windows.
- [ ] Precompiled release launches directly without running dotnet restore/publish on the user's PC.
- [ ] No PowerShell or Command Prompt build window is required for the final precompiled release.
