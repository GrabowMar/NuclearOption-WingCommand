# Exercise production reflow and page-switch methods without loading Unity.
$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Ui/WmcScreen.TacticalNavigation.cs" -Raw
$tokens = Get-Content "$PSScriptRoot/../Avionics/AvionicsTokens.cs" -Raw
$screen = Get-Content "$PSScriptRoot/../Ui/WmcScreen.cs" -Raw
$groups = Get-Content "$PSScriptRoot/../Ui/WmcScreen.Groups.cs" -Raw
function Get-ConstExpression([string]$text, [string]$name) {
    $match = [regex]::Match($text, "(?m)^\s*(?:public|private)\s+const\s+(?:float|int)\s+$name\s*=\s*(?<value>[^;]+);")
    if (!$match.Success) { throw "Missing layout constant: $name" }
    $match.Groups['value'].Value.Trim()
}
$methods = foreach ($name in @('ToggleRosterExpanded', 'ReflowTactical', 'FitTacticalViewport', 'SetTacticalDeck')) {
    $match = [regex]::Match($source, "(?ms)^        private static void $name\(.*?^        }")
    if (!$match.Success) { throw "Missing layout method: $name" }
    $method = $match.Value
    $method = $method.Replace('tacticalDeckPages[i]?.gameObject.SetActive(i == index);',
        'if (tacticalDeckPages[i] != null) tacticalDeckPages[i].gameObject.SetActive(i == index);')
    $method = $method.Replace('tacticalDeckTabs[i]?.SetLatched(i == index);',
        'if (tacticalDeckTabs[i] != null) tacticalDeckTabs[i].SetLatched(i == index);')
    $method.Replace('WingCommandManager.Instance?.CancelMapOrder(notify: false);',
        'if (WingCommandManager.Instance != null) WingCommandManager.Instance.CancelMapOrder(notify: false);')
}
$rosterRows = Get-ConstExpression $screen 'RosterRowsPerPage'
$expandedRows = Get-ConstExpression $source 'ExpandedRosterRows'
$rowPitch = Get-ConstExpression $tokens 'RowPitch'
$rowHeight = Get-ConstExpression $tokens 'RowHeight'
$panelWidth = Get-ConstExpression $tokens 'PanelWidth'
$panelHeight = Get-ConstExpression $tokens 'PanelHeightMax'
$pad = Get-ConstExpression $tokens 'Pad'
$gap = Get-ConstExpression $tokens 'Gap'
$space2 = Get-ConstExpression $tokens 'Space2'
$space5 = Get-ConstExpression $tokens 'Space5'
$statusStripHeight = Get-ConstExpression $tokens 'StatusStripHeight'
$flightGroupsHeight = Get-ConstExpression $groups 'FlightGroupsHeight'
$boundary = @'
using System;
class Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
class Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h) { this.x=x; this.y=y; width=w; height=h; } }
class GameObject { public bool Active; public void SetActive(bool b) { Active=b; } }
class RectTransform { public Vector2 anchoredPosition=new Vector2(0,0), sizeDelta=new Vector2(0,0); public GameObject gameObject=new GameObject(); }
class ScrollRect { public float verticalNormalizedPosition; public void StopMovement() {} }
class WingButton { public bool Latched; public void SetLatched(bool b) { Latched=b; } }
class WingCommandManager { public static WingCommandManager Instance=new WingCommandManager(); public int Cancellations; public void CancelMapOrder(bool notify) { Cancellations++; } }
static class Mathf {
    public static float Max(float a,float b) { return Math.Max(a,b); }
    public static int Clamp(int v,int min,int max) { return Math.Min(Math.Max(v,min),max); }
}
public static class TacticalLayoutChecks {
'@ + @"
    const int RosterRowsPerPage=$rosterRows, ExpandedRosterRows=$expandedRows;
    const float RowPitch=$rowPitch, RowHeight=$rowHeight, PanelWidth=$panelWidth, Pad=$pad, Gap=$gap,
        Space2=$space2, Space5=$space5, StatusStripHeight=$statusStripHeight, TacticalButtonHeight=RowHeight;
    const float FlightGroupsHeight=$flightGroupsHeight;
"@ + @'
    static bool rosterExpanded;
    static int rosterPage, tacticalDeck;
    static float nextRefresh, panelHeight=__PANEL_HEIGHT__, tacticalTop=-174, tacticalCommandsTop=-226, tacticalCollapsedHeight=700;
    static RectTransform flightGroupsRoot=new RectTransform();
    static RectTransform rosterArea=new RectTransform(), tacticalCommands=new RectTransform(), tacticalContent=new RectTransform(),
        tacticalViewport=new RectTransform(), tacticalScrollTrack=new RectTransform();
    static ScrollRect tacticalScroll=new ScrollRect();
    static RectTransform[] tacticalDeckPages={new RectTransform(),new RectTransform(),new RectTransform()};
    static WingButton[] tacticalDeckTabs={new WingButton(),new WingButton(),new WingButton()};
    static float[] tacticalDeckBottoms={-420f,-620f,-390f};
    static void CloseFlightGroupEditor() {}
    static object Wing() { return null; }
    static void RefreshTactical(object wing) {}
    static void Place(RectTransform target, Rect rect) { target.anchoredPosition=new Vector2(rect.x,rect.y); target.sizeDelta=new Vector2(rect.width,rect.height); }
'@ -replace '__PANEL_HEIGHT__', $panelHeight
$checks = @'
    static void Check(bool pass,string message) { if (!pass) throw new Exception(message); }
    public static void Run() {
        FitTacticalViewport(); ReflowTactical();
        float viewportHeight=tacticalViewport.sizeDelta.y;
        float extra=(ExpandedRosterRows-RosterRowsPerPage)*RowPitch;
        Check(RosterRowsPerPage==4 && ExpandedRosterRows==6, "Tactical roster uses four compact rows and six expanded rows");
        Check(rosterArea.sizeDelta.y==RosterRowsPerPage*RowPitch && tacticalCommands.anchoredPosition.y==tacticalCommandsTop, "Default roster rows");
        Check(!flightGroupsRoot.gameObject.Active, "Collapsed flight has no group controls");
        Check(tacticalViewport.sizeDelta.x==PanelWidth && tacticalContent.sizeDelta.x==PanelWidth, "Viewport and content use the current panel width");
        Check(tacticalScrollTrack.anchoredPosition.x==PanelWidth-12f && tacticalScrollTrack.sizeDelta.x==8f, "Scroll track stays on the panel edge");
        Check(tacticalTop-viewportHeight==-(panelHeight-Pad-StatusStripHeight-Space2), "Viewport ends above pinned footer");
        int compactPage=3;
        int first=compactPage*RosterRowsPerPage;
        rosterPage=compactPage; ToggleRosterExpanded();
        int expandedPage=first/ExpandedRosterRows;
        Check(rosterExpanded && rosterPage==expandedPage, "Expansion preserves the first visible aircraft page");
        Check(rosterArea.sizeDelta.y==ExpandedRosterRows*RowPitch && tacticalCommands.anchoredPosition.y==tacticalCommandsTop-extra-FlightGroupsHeight, "Expanded rows push commands down without overlap");
        Check(tacticalContent.sizeDelta.y==tacticalCollapsedHeight+extra+FlightGroupsHeight && tacticalViewport.sizeDelta.y==viewportHeight, "Only scroll content grows");
        Check(flightGroupsRoot.gameObject.Active && flightGroupsRoot.anchoredPosition.y==tacticalCommandsTop-extra, "Groups follow expanded aircraft rows");
        nextRefresh=5f; SetTacticalDeck(0);
        float directivesHeight=tacticalContent.sizeDelta.y;
        Check(tacticalCommands.sizeDelta.y==-tacticalDeckBottoms[0] && tacticalCollapsedHeight==-tacticalCommandsTop-tacticalDeckBottoms[0], "Directives deck sets its own content height");
        SetTacticalDeck(1);
        Check(tacticalDeckPages.Length==3 && tacticalDeckTabs.Length==3, "Tactical navigation has three decks");
        for (int i=0;i<3;i++) Check(tacticalDeckPages[i].gameObject.Active==(i==1) && tacticalDeckTabs[i].Latched==(i==1), "Exactly one tactical deck");
        Check(tacticalCommands.sizeDelta.y==-tacticalDeckBottoms[1] && tacticalContent.sizeDelta.y>directivesHeight && tacticalContent.sizeDelta.y==tacticalCollapsedHeight+extra+FlightGroupsHeight, "Geometry deck grows only the scroll content to its active height");
        Check(WingCommandManager.Instance.Cancellations==2 && rosterPage==expandedPage && tacticalDeck==1 && nextRefresh==0f, "Deck switches disarm the map tool without changing roster pagination");
        SetTacticalDeck(2);
        for (int i=0;i<3;i++) Check(tacticalDeckPages[i].gameObject.Active==(i==2) && tacticalDeckTabs[i].Latched==(i==2), "Route nodes is an independent deck");
        Check(tacticalCommands.sizeDelta.y==-tacticalDeckBottoms[2], "Route nodes uses its own height");
        ToggleRosterExpanded();
        Check(!rosterExpanded && rosterPage==compactPage && tacticalContent.sizeDelta.y==tacticalCollapsedHeight, "Collapse restores compact layout and aligned page");
        Check(!flightGroupsRoot.gameObject.Active, "Collapsing hides group controls again");
    }
}
'@
Add-Type -TypeDefinition ($boundary + ($methods -join "`n") + $checks) -IgnoreWarnings -WarningAction SilentlyContinue
[TacticalLayoutChecks]::Run()
Write-Output 'Tactical layout and pagination checks passed.'
