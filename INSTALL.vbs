Option Explicit

Dim shell, fso, src, dest, destApp, ico, launch, desktop, startMenu, logDir
Dim q

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

src = fso.GetParentFolderName(WScript.ScriptFullName)
dest = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLink")
destApp = fso.BuildPath(dest, "app")
ico = fso.BuildPath(dest, "iPhoneLink.ico")
launch = fso.BuildPath(dest, "Launch.vbs")
desktop = shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink.lnk")
startMenu = shell.ExpandEnvironmentStrings("%APPDATA%\Microsoft\Windows\Start Menu\Programs\iPhoneLink.lnk")
logDir = dest
q = Chr(34)

If Not fso.FolderExists(fso.BuildPath(src, "app")) Then
    MsgBox "This installer is missing the app folder. Unzip the whole package first, then double-click INSTALL.", vbCritical, "iPhoneLink"
    WScript.Quit 1
End If

If Not fso.FolderExists(dest) Then fso.CreateFolder dest
If fso.FolderExists(destApp) Then fso.DeleteFolder destApp, True
fso.CopyFolder fso.BuildPath(src, "app"), destApp
fso.CopyFile fso.BuildPath(src, "Launch.vbs"), launch, True
If fso.FileExists(fso.BuildPath(src, "iPhoneLink.ico")) Then
    fso.CopyFile fso.BuildPath(src, "iPhoneLink.ico"), ico, True
End If

MakeShortcut desktop
MakeShortcut startMenu

shell.Run "wscript.exe " & q & launch & q, 0, False
WScript.Quit 0

Sub MakeShortcut(path)
    Dim sc
    Set sc = shell.CreateShortcut(path)
    sc.TargetPath = "wscript.exe"
    sc.Arguments = q & launch & q
    sc.WorkingDirectory = dest
    sc.WindowStyle = 7
    sc.Description = "iPhoneLink"
    If fso.FileExists(ico) Then sc.IconLocation = ico
    sc.Save
End Sub
