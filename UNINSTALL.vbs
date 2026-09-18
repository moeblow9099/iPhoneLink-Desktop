Option Explicit
Dim shell, fso, dest, app
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
On Error Resume Next
shell.Run "taskkill /IM iPhoneLinkCRM.exe /F", 0, True
fso.DeleteFile shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink.lnk"), True
fso.DeleteFile shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink CRM.lnk"), True
dest = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLinkCRM")
If fso.FolderExists(dest) Then fso.DeleteFolder dest, True
dest = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLink")
If fso.FolderExists(dest) Then fso.DeleteFolder dest, True
On Error GoTo 0
MsgBox "iPhoneLink was removed.", vbInformation, "iPhoneLink"
