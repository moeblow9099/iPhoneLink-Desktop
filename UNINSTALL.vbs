Option Explicit
Dim shell, fso, dest
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
dest = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLink")
On Error Resume Next
fso.DeleteFile shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink.lnk"), True
fso.DeleteFile shell.ExpandEnvironmentStrings("%APPDATA%\Microsoft\Windows\Start Menu\Programs\iPhoneLink.lnk"), True
If fso.FolderExists(dest) Then fso.DeleteFolder dest, True
On Error GoTo 0
MsgBox "iPhoneLink was removed.", vbInformation, "iPhoneLink"
