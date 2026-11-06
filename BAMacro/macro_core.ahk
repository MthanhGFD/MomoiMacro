; ==============================================================================
; BA Macro v2.0 - Core Script (AutoHotkey v1.1)
;   Ctrl + 1 : Bật / Tắt macro
;   Ctrl + 2 : Bật / Tắt chế độ Record (gán vị trí cho phím)
;   Record ON  : bấm phím bất kỳ trong danh sách -> lưu vị trí chuột hiện tại cho phím đó
;   Macro ON   : bấm phím đã gán -> click vào vị trí đã lưu
;   Q / W / E  : Send 1/2/3 -> delay -> Click tại vị trí chuột hiện tại
; Mọi phím macro CHỈ hoạt động khi cửa sổ game đang active và không bị minimize.
; ==============================================================================

#NoEnv
#SingleInstance Force
#Persistent
#UseHook On
SetBatchLines -1
SetKeyDelay, -1, -1
SetMouseDelay, -1
SendMode Input
CoordMode, Mouse, Client
CoordMode, ToolTip, Screen

; Args (optional): 1 = path to coords.ini, 2 = PID of the BAMacro UI (engine exits when UI closes)
global configFile := (A_Args.Length() >= 1) ? A_Args[1] : A_ScriptDir "\coords.ini"
global ParentPid  := (A_Args.Length() >= 2) ? A_Args[2] : 0
global TargetExe  := "BlueArchive.exe"
global DelayMs    := 20
global running    := false
global capture    := false
global coordMap   := {}

LoadConfig()
SaveState()
OnExit("OnScriptExit")
if (ParentPid)
    SetTimer, WatchParent, 1000
return

WatchParent:
    Process, Exist, %ParentPid%
    if (!ErrorLevel)
        ExitApp
return

; ==============================================================================
; Hotkey điều khiển (global)
; ==============================================================================
^1::
    running := !running
    if (running) {
        LoadConfig()
        SoundBeep, 900, 80
        ShowTip("MomoiMacro: ON")
    } else {
        capture := false
        SoundBeep, 500, 120
        ShowTip("MomoiMacro: OFF")
    }
    SaveState()
return

^2::
    capture := !capture
    if (capture) {
        SoundBeep, 1200, 60
        ShowTip("Record: ON - Click a key to save mouse position.")
    } else {
        SoundBeep, 700, 60
        ShowTip("Record: OFF")
    }
    SaveState()
return

; ==============================================================================
; Các phím chỉ hoạt động khi game đang active & không minimize
; ==============================================================================
#If BAActive()

$q:: SkillCast("q", "1")
$w:: SkillCast("w", "2")
$e:: SkillCast("e", "3")

$a::
$b::
$c::
$d::
$f::
$g::
$h::
$i::
$j::
$k::
$l::
$m::
$n::
$o::
$p::
$r::
$s::
$t::
$u::
$v::
$x::
$y::
$z::
$xbutton1::
$xbutton2::
$rbutton::
    key := RegExReplace(A_ThisHotkey, "[\$~*]")
    StringLower, key, key

    if (capture) {
        CaptureKey(key)
        return
    }

    if (running && coordMap.HasKey(key)) {
        ClickKey(key)
        return
    }

    Send, {%key%} ; Gửi lại phím gốc nếu macro tắt / phím chưa được gán
return

#If

; ==============================================================================
; Functions
; ==============================================================================
BAActive() {
    hwnd := WinActive("ahk_exe " TargetExe)
    if (!hwnd)
        return false
    WinGet, mm, MinMax, ahk_id %hwnd%
    return (mm != -1)
}

; Kích thước vùng client (không tính viền + thanh tiêu đề -> không bị lệch/âm khi chơi cửa sổ)
GetClientSize(hwnd, ByRef w, ByRef h) {
    VarSetCapacity(rc, 16, 0)
    DllCall("GetClientRect", "Ptr", hwnd, "Ptr", &rc)
    w := NumGet(rc, 8, "Int")
    h := NumGet(rc, 12, "Int")
}

CaptureKey(key) {
    hwnd := WinActive("ahk_exe " TargetExe)
    GetClientSize(hwnd, w, h)
    if (w <= 0 || h <= 0)
        return

    MouseGetPos, x, y ; toạ độ theo client của cửa sổ game
    if (x < 0 || y < 0 || x >= w || y >= h) {
        ShowTip("The selected position is not inside the game window.")
        return
    }

    rx := Round(x / w, 4)
    ry := Round(y / h, 4)

    coordMap[key] := [rx, ry]
    IniWrite, %rx%`,%ry%, %configFile%, Keys, %key%

    ShowTip("Set " key " -> (" Round(rx, 3) ", " Round(ry, 3) ")", 800)
}

ClickKey(key) {
    hwnd := WinActive("ahk_exe " TargetExe)
    GetClientSize(hwnd, w, h)
    if (w <= 0 || h <= 0)
        return

    pos := coordMap[key]
    x := Round(pos[1] * w)
    y := Round(pos[2] * h)
    Click, %x%, %y%
}

SkillCast(key, slot) {
    if (!running) {
        Send, {%key%}
        return
    }
    Send, %slot%
    Sleep, %DelayMs%
    Click
}

LoadConfig() {
    IniRead, TargetExe, %configFile%, Settings, TargetExe, BlueArchive.exe
    IniRead, DelayMs,   %configFile%, Settings, DelayMs, 20

    coordMap := {}
    IniRead, section, %configFile%, Keys
    Loop, Parse, section, `n, `r
    {
        eq := InStr(A_LoopField, "=")
        if (!eq)
            continue
        k := SubStr(A_LoopField, 1, eq - 1)
        v := StrSplit(SubStr(A_LoopField, eq + 1), ",")
        if (v.Length() = 2)
            coordMap[k] := [v[1] + 0, v[2] + 0]
    }
}

SaveState() {
    state := running ? "RUNNING" : "STOPPED"
    cap   := capture ? 1 : 0
    IniWrite, %state%, %configFile%, Status, State
    IniWrite, %cap%,   %configFile%, Status, Capture
}

OnScriptExit() {
    running := false
    capture := false
    SaveState()
}

ShowTip(msg, duration := 1200) {
    ToolTip, %msg%
    SetTimer, RemoveToolTip, -%duration%
}

RemoveToolTip:
    ToolTip
return
