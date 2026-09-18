Option Explicit

Dim shell, fso, root, publishCmd, appDir, appExe, shortcutPath, logDir, logPath
Dim command, exitCode, logText, q

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
publishCmd = fso.BuildPath(root, "tools\publish.cmd")
appDir = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLinkCRM\App")
appExe = fso.BuildPath(appDir, "iPhoneLinkCRM.exe")
shortcutPath = shell.ExpandEnvironmentStrings("%USERPROFILE%\Desktop\iPhoneLink CRM.lnk")
logDir = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLinkCRM")
logPath = fso.BuildPath(logDir, "install.log")
q = Chr(34)

If Not fso.FileExists(publishCmd) Then
    MsgBox "The source publish tool is missing: " & publishCmd, vbCritical, "iPhoneLink CRM"
    WScript.Quit 1
End If

If Not fso.FolderExists(logDir) Then
    fso.CreateFolder(logDir)
End If

' This is a source package installer. Run the build command hidden so no
' PowerShell or Command Prompt window is displayed to the user.
command = q & shell.ExpandEnvironmentStrings("%COMSPEC%") & q & " /d /c call " & q & publishCmd & q & " > " & q & logPath & q & " 2>&1"
exitCode = shell.Run(command, 0, True)

If exitCode <> 0 Or Not fso.FileExists(appExe) Then
    logText = "The app could not be built or installed."
    If fso.FileExists(logPath) Then
        logText = logText & vbCrLf & vbCrLf & "Build log:" & vbCrLf & logPath
    End If
    MsgBox logText, vbCritical, "iPhoneLink CRM install failed"
    WScript.Quit 1
End If

Dim shortcut
Set shortcut = shell.CreateShortcut(shortcutPath)
shortcut.TargetPath = appExe
shortcut.WorkingDirectory = appDir
shortcut.IconLocation = appExe
shortcut.Description = "iPhoneLink CRM"
shortcut.Save

shell.Run """" & appExe & """", 1, False
WScript.Quit 0
