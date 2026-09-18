# iPhoneLink session

Last updated: 2026-09-18

## What this is

Windows desktop iPhone/Android PC dialer. Floating 3D iPhone shell. Keypad, Messages, Recents, Contacts, Voicemail, in-call. Bluetooth MAP / PBAP / HFP auto-sync. No notification/sync toggles. Light mode default.

Repo: https://github.com/moeblow9099/iPhoneLink-Desktop  
Latest Windows package: https://github.com/moeblow9099/iPhoneLink-Desktop/releases/download/v2.0/iPhoneLink-Desktop-Setup.zip

## Install (v2 — real app)

1. Install .NET 8 SDK if needed: https://dotnet.microsoft.com/download/dotnet/8.0
2. Download the v2 zip above.
3. Unzip.
4. Double-click `INSTALL.vbs` only. Do not use `Launch.vbs`.
5. Desktop shortcut **iPhoneLink** opens `iPhoneLinkCRM.exe` (WinForms + WebView2). Hidden engine runs Bluetooth + local bridge at `http://127.0.0.1:8765`.

v1 zip was HTML-only and opened in the browser. Do not use v1.

## Architecture

- Visible UI: `engine/src/PhoneLinkDiag/UI/ShellForm.cs` loads `engine/src/PhoneLinkDiag/Assets/shell/index.html` in WebView2.
- Backend: MAP250 (`MainForm` hidden). Bridge token in `%LOCALAPPDATA%\iPhoneLinkCRM\bridge-token.txt`. Shell sends `X-iPhoneLink-Token`.
- Endpoints: `/health` `/devices` `/messages` `/contacts` `/calls` `/send-sms` `/call`.
- Publish: `engine/tools/publish.cmd` → `%LOCALAPPDATA%\iPhoneLinkCRM\App\iPhoneLinkCRM.exe`.

## Product rules (user)

- Desktop app, not a browser tab.
- One-click install, desktop shortcut, no terminal.
- Extract all iPhone iMessage/SMS threads, call log, contacts.
- Compact shell, smaller than Phone Link (scale 0.88, 354×769 stage).
- Light mode default.
- Inter, no neon/pink, no fake “HFP connected” labels.
- iMessage `#0A84FF`, SMS `#30D158`, white bubble text.
- 3D drag from center, spring back straight. Titanium casing, real sides.
- Tabs: Messages, Recents, Contacts, Keypad, Voicemail. No pill. No home-bar line.
- Keypad first. Never block on “Connecting”.
- Vercel `*.dial3.vercel.app` is SSO-locked for antonio1976668888@gmail.com. Use GitHub/jsDelivr.

## Design sandbox (Grok preview)

Workspace `/workspace` React/TanStack app on `:8080`:

- `src/components/phone-frame.tsx` — 3D orbit vs scale split
- `src/components/crm-app.tsx` — keypad immediately
- `src/lib/sync-store.ts` — `readTheme()` defaults light
- `src/styles.css` — titanium bezel + 3D faces
- Extractor: `scripts/extract-iphone-messages.mjs`

## Git

- User: moeblow9099
- v2.0 tag: Windows engine + WebView2 shell
- Public HTML preview (not the app): https://cdn.jsdelivr.net/gh/moeblow9099/iPhoneLink-Desktop@main/index.html

## Next

- Confirm INSTALL builds exe on the PC (.NET 8 SDK + WebView2 Runtime).
- Paired iPhone/Android should fill Messages / Recents / Contacts from MAP/PBAP.
- Keypad green button POSTs `/call` (HFP).
- If INSTALL fails, read `%LOCALAPPDATA%\iPhoneLinkCRM\install.log`.
