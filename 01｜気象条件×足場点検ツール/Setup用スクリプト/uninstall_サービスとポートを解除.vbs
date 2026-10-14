' ============================================================
'  ScaffoldAlert - uninstall helper
' ============================================================
'  アンインストールのときに呼ばれる「解除係」。
'
'  install_サービスとポートを登録.vbs が登録したものを、
'  すべて元に戻す。
'
'  やること
'    1. Windows サービス「ScaffoldAlert」を止めて登録を消す
'    2. ファイアウォールの穴（TCP 5080）を閉じる
'
'  ★ここでは失敗しても止めない
'    すでに手で消されていることもある。
'    「消えていない」より「二重に消そうとした」ほうが害が小さいので、
'    エラーは無視して最後まで進める。
'    途中で止めると、ファイルだけ残って消せなくなる。
'
'  ★呼び出し方（Visual Studio 側の設定）
'    Custom Actions → Uninstall → このファイル
' ============================================================

Option Explicit

Const SERVICE_NAME  = "ScaffoldAlert"
Const FIREWALL_NAME = "ScaffoldAlert"

Dim shell
Set shell = CreateObject("WScript.Shell")

On Error Resume Next

' --- 1. Stop and remove the service ---
shell.Run "sc.exe stop "   & SERVICE_NAME, 0, True

 ' Give Windows a moment to release the executable.
' WScript.Sleep cannot be used here: inside an MSI custom action
' there is no WScript object. Waiting on ping instead is the
' usual workaround.
shell.Run "cmd.exe /c ping -n 4 127.0.0.1", 0, True

shell.Run "sc.exe delete " & SERVICE_NAME, 0, True

' --- 2. Close the firewall hole ---
shell.Run "netsh.exe advfirewall firewall delete rule name=" & _
          Chr(34) & FIREWALL_NAME & Chr(34), 0, True

On Error GoTo 0
