' ============================================================
'  ScaffoldAlert - install helper
' ============================================================
'  Visual Studio の Setup Project から呼ばれる「登録係」。
'
'  Setup Project は、ファイルを置くことはできるが
'  Windows サービスの登録ができない。
'  その部分だけを、この小さなスクリプトが行う。
'
'  やること
'    1. Windows サービス「ScaffoldAlert」を登録して開始する
'    2. ファイアウォールに TCP 5080 の穴を開ける（社内LANのみ）
'
'  ★中身を英語だけで書いている理由
'    このファイルはインストーラーに埋め込まれて実行される。
'    日本語が混ざると、PCの文字コード設定によって
'    文字化けし、動かなくなることがあるため。
'
'  ★呼び出し方（Visual Studio 側の設定）
'    Custom Actions → Install → このファイル
'    CustomActionData に  [TARGETDIR]  を設定すること。
'    設定しないと、どこに入れたのか分からず登録できない。
' ============================================================

Option Explicit

Const SERVICE_NAME  = "ScaffoldAlert"
Const SERVICE_DISP  = "ScaffoldAlert (Scaffold Inspection)"
Const SERVICE_DESC  = "Scaffold inspection duty management. Open http://<this pc>:5080/ in a browser."
Const FIREWALL_NAME = "ScaffoldAlert"
Const PORT          = "5080"

Dim shell, target, exePath, cmd, rc

Set shell = CreateObject("WScript.Shell")

' --- Where was it installed? ---
target = Session.Property("CustomActionData")

If target = "" Then
    ' Fall back to the default location.
    target = shell.ExpandEnvironmentStrings("%ProgramFiles%") & "\ScaffoldAlert\"
End If

If Right(target, 1) <> "\" Then
    target = target & "\"
End If

exePath = target & "web\ScaffoldAlert.Web.exe"

' --- 1. Remove an old registration, if any ---
'     Makes re-installing safe. Errors here are expected the
'     first time, so they are ignored.
shell.Run "sc.exe stop "   & SERVICE_NAME, 0, True
shell.Run "sc.exe delete " & SERVICE_NAME, 0, True

' --- 2. Register the service ---
cmd = "sc.exe create " & SERVICE_NAME & _
      " binPath= "     & Chr(34) & exePath & Chr(34) & _
      " start= auto"                                & _
      " DisplayName= " & Chr(34) & SERVICE_DISP & Chr(34)

rc = shell.Run(cmd, 0, True)

If rc <> 0 Then
    Err.Raise 5, "ScaffoldAlert", _
        "Could not register the Windows service. sc.exe returned " & rc
End If

' --- 3. Description and auto-restart ---
'     If the service stops unexpectedly, try again after 60 s.
shell.Run "sc.exe description " & SERVICE_NAME & " " & _
          Chr(34) & SERVICE_DESC & Chr(34), 0, True

shell.Run "sc.exe failure " & SERVICE_NAME & _
          " reset= 86400 actions= restart/60000/restart/60000/restart/60000", _
          0, True

' --- 4. Firewall: allow TCP 5080 from the local network ---
shell.Run "netsh.exe advfirewall firewall delete rule name=" & _
          Chr(34) & FIREWALL_NAME & Chr(34), 0, True

cmd = "netsh.exe advfirewall firewall add rule name=" & _
      Chr(34) & FIREWALL_NAME & Chr(34) & _
      " dir=in action=allow protocol=TCP localport=" & PORT & _
      " profile=domain,private"

shell.Run cmd, 0, True

' --- 5. Start it ---
rc = shell.Run("sc.exe start " & SERVICE_NAME, 0, True)

If rc <> 0 Then
    Err.Raise 5, "ScaffoldAlert", _
        "The service was registered but did not start. sc.exe returned " & rc & _
        ". Check that the database is reachable."
End If
