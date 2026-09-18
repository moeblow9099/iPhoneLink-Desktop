Option Explicit

Dim shell, fso, appDir, cacheDir, shortcutPath
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

appDir = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLinkCRM")
cacheDir = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\PhoneLinkDiag")
shortcutPath = shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink CRM.lnk")

On Error Resume Next
If fso.FileExists(shortcutPath) Then fso.DeleteFile shortcutPath, True
If fso.FolderExists(appDir) Then fso.DeleteFolder appDir, True
If fso.FolderExists(cacheDir) Then fso.DeleteFolder cacheDir, True
On Error GoTo 0

MsgBox "iPhoneLink CRM and its local cache were removed.", vbInformation, "iPhoneLink CRM"
WScript.Quit 0
