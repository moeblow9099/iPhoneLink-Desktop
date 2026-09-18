Option Explicit

Dim shell, fso, appExe
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

appExe = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\iPhoneLinkCRM\App\iPhoneLinkCRM.exe")
If Not fso.FileExists(appExe) Then
    MsgBox "iPhoneLink CRM is not installed yet. Run INSTALL-iPhoneLinkCRM.vbs once.", vbInformation, "iPhoneLink CRM"
    WScript.Quit 1
End If

shell.Run """" & appExe & """", 1, False
WScript.Quit 0
