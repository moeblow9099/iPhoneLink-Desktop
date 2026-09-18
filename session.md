# iPhoneLink session

Last updated: 2026-09-18

## What this is

Windows desktop iPhone/Android PC dialer. Floating 3D iPhone shell. Keypad, Messages, Recents, Contacts, Voicemail, in-call. Bluetooth MAP / PBAP / HFP auto-sync. No notification/sync toggles. Light mode default.

Repo: https://github.com/moeblow9099/iPhoneLink-Desktop

Latest Windows package: https://github.com/moeblow9099/iPhoneLink-Desktop/releases/download/v2.0/iPhoneLink-Desktop-Setup.zip

## Source of truth

* Packaged installer still v2.0 zip.
* Current source on `main` is **v2.1**.
* Do not overwrite v2.0. Next packaged zip must be a new tag.

## Install (v2 — real app)

1) Install .NET 8 SDK if needed: https://dotnet.microsoft.com/download/dotnet/8.0
2) Download the v2 zip above, or clone `main`.
3) Unzip / open the folder.
4) Double-click INSTALL.vbs only. Do not use Launch.vbs.
5) Desktop shortcut **iPhoneLink** opens iPhoneLinkCRM.exe (WinForms + WebView2). Hidden engine runs Bluetooth + local bridge at http://127.0.0.1:8765 .

v1 zip was HTML-only and opened in the browser. Do not use v1.

## Architecture

* Visible UI: engine/src/PhoneLinkDiag/UI/ShellForm.cs loads engine/src/PhoneLinkDiag/Assets/shell/index.html in WebView2.
* Backend: MAP250 (MainForm hidden). Bridge token in %LOCALAPPDATA%\iPhoneLinkCRM\bridge-token.txt . Shell sends X-iPhoneLink-Token .
* Endpoints: /health /devices /messages /contacts /calls /send-sms /call /hangup .
* Publish: engine/tools/publish.cmd → %LOCALAPPDATA%\iPhoneLinkCRM\App\iPhoneLinkCRM.exe .

## Product rules (user)

* Desktop app, not a browser tab.
* One-click install, desktop shortcut, no terminal.
* Extract all iPhone iMessage/SMS threads, call log, contacts.
* Compact shell, smaller than Phone Link (scale 0.88, 354×769 stage).
* Light mode default.
* Inter, no neon/pink, no fake “HFP connected” labels.
* iMessage #0A84FF , SMS #30D158 , white bubble text.
* 3D drag from center, spring back straight. Titanium casing, real sides.
* Tabs: Messages, Recents, Contacts, Keypad, Voicemail. No pill. No home-bar line.
* Keypad first. Never block on “Connecting”.
* Vercel *.dial3.vercel.app is SSO-locked for antonio1976668888@gmail.com . Use GitHub/jsDelivr.

## v2.1 source changes (this chat)

* Contacts auto-sync is on. Auto-sync always pulls PBAP contacts + call history.
* Program.cs waits for the bridge token before the shell loads.
* ShellForm re-reads the token file if the first pass is empty.
* Live shell starts empty. No demo people when the token is present.
* Green keypad, Recents, and Contacts POST /call with a real number.
* Hang-up POSTs /hangup.
* Message threads group by peer (From/To), not only From.
* 3D tilt ignores buttons, keys, and list rows.

## Git

* User: moeblow9099
* v2.0 tag: Windows engine + WebView2 shell (packaged zip)
* Public HTML preview (not the app): https://cdn.jsdelivr.net/gh/moeblow9099/iPhoneLink-Desktop@main/index.html

## Next

* On the Windows PC: INSTALL.vbs against this `main` source.
* Confirm Messages / Recents / Contacts fill from a paired phone.
* Confirm keypad green button places an HFP call.
* Confirm hang-up ends the call.
* If INSTALL fails, read %LOCALAPPDATA%\iPhoneLinkCRM\install.log .
* Hardware Bluetooth and WebView2 launch are unverified in this Linux sandbox.
* Later package a new v2.1 zip. Do not replace the v2.0 asset.
