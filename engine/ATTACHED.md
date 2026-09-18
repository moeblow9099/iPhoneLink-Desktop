# MAP250 attached engine

Audited before attach:

- Source-only Build 010, 31 C# files, 8,326 lines
- No `bin`, `obj`, DLL, EXE, or decompiled Phone Link code
- No TODO / FIXME / NotImplemented
- HFP dial locked to `ATD{number};`, hang-up `AT+CHUP`
- MAP default 250, multi-phone `DeviceId` isolation
- Local bridge `http://127.0.0.1:8765/` with token auth

Runtime note: this Linux preview cannot open Windows Bluetooth RFCOMM.
`src/lib/bridge` is the attached engine contract running here:

- Auto-sync messages, call history, **and contacts** on connect
- No Sync Contacts toggle
- No notification-permission toggle
- Same bridge actions: health, devices, messages, contacts, calls, send-sms, call
