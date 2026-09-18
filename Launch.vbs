Option Explicit

Dim shell, fso, root, html, url, browser, cmd
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
html = fso.BuildPath(root, "app\index.html")
If Not fso.FileExists(html) Then
    MsgBox "iPhoneLink is missing. Run INSTALL again.", vbCritical, "iPhoneLink"
    WScript.Quit 1
End If

url = "file:///" & Replace(Replace(html, "\", "/"), " ", "%20")
browser = FindBrowser()
If browser = "" Then
    shell.Run q(url), 1, False
    WScript.Quit 0
End If

cmd = q(browser) & " --app=" & q(url) & " --window-size=480,940 --window-position=240,40 --disable-features=TranslateUI" & _
      " --user-data-dir=" & q(fso.BuildPath(root, "profile"))
shell.Run cmd, 0, False
WScript.Quit 0

Function q(value)
    q = Chr(34) & value & Chr(34)
End Function

Function FindBrowser()
    Dim names, i, path
    names = Array( _
        shell.ExpandEnvironmentStrings("%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"), _
        shell.ExpandEnvironmentStrings("%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"), _
        shell.ExpandEnvironmentStrings("%LocalAppData%\Microsoft\Edge\Application\msedge.exe"), _
        shell.ExpandEnvironmentStrings("%ProgramFiles%\Google\Chrome\Application\chrome.exe"), _
        shell.ExpandEnvironmentStrings("%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"), _
        shell.ExpandEnvironmentStrings("%LocalAppData%\Google\Chrome\Application\chrome.exe") _
    )
    For i = 0 To UBound(names)
        path = names(i)
        If fso.FileExists(path) Then
            FindBrowser = path
            Exit Function
        End If
    Next
    FindBrowser = ""
End Function
