; ==============================================================================
; Blue Archive Macro - Neutron.ahk Edition (HTML/CSS/JS GUI)
; ==============================================================================

#NoEnv
#SingleInstance Force
#Persistent
SetBatchLines -1
SetKeyDelay, -1, -1
SetMouseDelay, -1
CoordMode, Mouse, Client

; Include Neutron library (standard Neutron.ahk)
#Include %A_ScriptDir%\Neutron.ahk

; Create Neutron GUI instance
neutron := new NeutronWindow()
neutron.Load("ui/index.html")
neutron.Gui("+LabelNeutronGui +Resize")
neutron.Show("w980 h640 Center", "BA Macro")

IniFile := A_ScriptDir . "\coords.ini"
Global TargetExe := "BlueArchive.exe"
Global DelayMs := 20
Global IsRunning := 0
Global neutronRef := neutron

InitIni()
RefreshUiCoords()
return

; ==============================================================================
; Neutron Window Events
; ==============================================================================
NeutronGuiClose:
    ExitApp
return

; ==============================================================================
; Exported JS / HTML Callable Methods
; ==============================================================================
ToggleMacro(state) {
    global IsRunning, neutronRef
    IsRunning := state
    if (IsRunning) {
        SoundBeep, 800, 100
        neutronRef.qs("#status-badge").className := "badge-active"
        neutronRef.qs("#status-dot").className := "dot-pink"
        neutronRef.qs("#status-text").innerText := "ACTIVE"
    } else {
        SoundBeep, 400, 150
        neutronRef.qs("#status-badge").className := "badge-ready"
        neutronRef.qs("#status-dot").className := "dot-green"
        neutronRef.qs("#status-text").innerText := "READY"
    }
}

OpenIni() {
    global IniFile
    Run, %IniFile%
}

; ==============================================================================
; Toggle Start / Stop Macro (Ctrl + 1)
; ==============================================================================
^1:: ToggleMacro(!IsRunning)

+q:: CaptureCoord("Q")
+w:: CaptureCoord("W")
+e:: CaptureCoord("E")

$q::
    if (!IsRunning) {
        Send, q
        return
    }
    CastAtSavedCoord("Q")
return

$w::
    if (!IsRunning) {
        Send, w
        return
    }
    CastAtSavedCoord("W")
return

$e::
    if (!IsRunning) {
        Send, e
        return
    }
    CastAtSavedCoord("E")
return

; ==============================================================================
; Core Logic Functions
; ==============================================================================
CaptureCoord(keyName) {
    global IniFile
    MouseGetPos, mX, mY, winHwnd
    WinGet, procName, ProcessName, ahk_id %winHwnd%
    
    VarSetCapacity(rc, 16, 0)
    DllCall("GetClientRect", "ptr", winHwnd, "ptr", &rc)
    clientW := NumGet(rc, 8, "int")
    clientH := NumGet(rc, 12, "int")

    if (clientW <= 0 || clientH <= 0)
        return

    relX := Round(mX / clientW, 4)
    relY := Round(mY / clientH, 4)

    IniWrite, %relX%, %IniFile%, Coordinates, %keyName%_X
    IniWrite, %relY%, %IniFile%, Coordinates, %keyName%_Y

    SoundBeep, 1200, 80
    RefreshUiCoords()
}

CastAtSavedCoord(keyName) {
    global IniFile
    IniRead, relX, %IniFile%, Coordinates, %keyName%_X, 0
    IniRead, relY, %IniFile%, Coordinates, %keyName%_Y, 0

    if (relX = "0" || relY = "0" || relX = "ERROR" || relY = "ERROR") {
        Send, %keyName%
        return
    }

    winHwnd := WinExist("A")
    VarSetCapacity(rc, 16, 0)
    DllCall("GetClientRect", "ptr", winHwnd, "ptr", &rc)
    clientW := NumGet(rc, 8, "int")
    clientH := NumGet(rc, 12, "int")

    targetX := Round(relX * clientW)
    targetY := Round(relY * clientH)

    ; Click chính xác vào vị trí mục tiêu đã lưu
    Click, %targetX%, %targetY%
}

RefreshUiCoords() {
    global IniFile, neutronRef
    IniRead, qx, %IniFile%, Coordinates, Q_X, 0.4500
    IniRead, qy, %IniFile%, Coordinates, Q_Y, 0.7500
    IniRead, wx, %IniFile%, Coordinates, W_X, 0.5200
    IniRead, wy, %IniFile%, Coordinates, W_Y, 0.7500
    IniRead, ex, %IniFile%, Coordinates, E_X, 0.5900
    IniRead, ey, %IniFile%, Coordinates, E_Y, 0.7500

    try {
        neutronRef.qs("#coordQ").innerText := Round(qx * 100, 1) . "%, " . Round(qy * 100, 1) . "%"
        neutronRef.qs("#coordW").innerText := Round(wx * 100, 1) . "%, " . Round(wy * 100, 1) . "%"
        neutronRef.qs("#coordE").innerText := Round(ex * 100, 1) . "%, " . Round(ey * 100, 1) . "%"
    }
}

InitIni() {
    global IniFile
    if (!FileExist(IniFile)) {
        IniWrite, BlueArchive.exe, %IniFile%, Settings, TargetExe
        IniWrite, 20, %IniFile%, Settings, DelayMs
        IniWrite, 0.4500, %IniFile%, Coordinates, Q_X
        IniWrite, 0.7500, %IniFile%, Coordinates, Q_Y
        IniWrite, 0.5200, %IniFile%, Coordinates, W_X
        IniWrite, 0.7500, %IniFile%, Coordinates, W_Y
        IniWrite, 0.5900, %IniFile%, Coordinates, E_X
        IniWrite, 0.7500, %IniFile%, Coordinates, E_Y
    }
}
