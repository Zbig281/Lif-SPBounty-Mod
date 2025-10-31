$SPB_UI::WinW = 800;
$SPB_UI::WinH = 520;
$SPB_UI::PadX = 12;
$SPB_UI::PadY = 64;
$SPB_UI::Background = "mods/SPBounty/ui/bounty_bg.png";
$SPB_UI_PayoutBusy = false;

if (!isObject(SPB_HeaderProfile)) new GuiControlProfile(SPB_HeaderProfile)
{
  fontType = "Arial Bold"; fontSize = 16;
  fontColor = "255 255 255";
  opaque = true; fillColor = "30 30 30 220";
  border = 1; borderColor = "0 0 0 220";
};

if (!isObject(SPB_ListProfile)) new GuiControlProfile(SPB_ListProfile)
{
  fontType = "Arial"; fontSize = 15;
  fontColor = "0 0 0";
  fontColorHL = "0 0 0";
  fontColorSEL = "255 255 255";
  opaque = true;
  fillColor = "235 235 235 240";
  fillColorHL = "210 210 210 240";
  fillColorSEL = "40 40 40 240";
  border = 1; borderColor = "25 25 25 200";
};

if (!isObject(SPB_InfoProfile)) new GuiControlProfile(SPB_InfoProfile)
{
  fontType = "Arial"; fontSize = 14; fontColor = "235 235 235";
};
if (!isObject(SPB_PanelProfile)) new GuiControlProfile(SPB_PanelProfile)
{
  opaque = true; fillColor = "0 0 0 150";
};

if (isObject(SPB_Window)) SPB_Window.delete();

new GuiControl(SPB_Window)
{
  profile = "GuiDefaultProfile";
  horizSizing = "width"; vertSizing = "height";
  position = "0 0"; extent = getWord(Canvas.getExtent(),0) SPC getWord(Canvas.getExtent(),1);
  visible  = "0";

  new GuiWindowCtrl(SPB_Win)
  {
    profile = "GuiWindowProfile";
    text = "Bounty";
    resizeWidth = "0"; 
    resizeHeight = "0"; 
    canMove = "1"; 
    canClose = "1";
    canMinimize = "0"; 
    canMaximize = "0";
    closeCommand = "SPB_Close();";

    // Background FIRST (draws under everything else)
    new GuiBitmapCtrl(SPB_BG) { profile="GuiDefaultProfile"; };
    // Readability panel above background
    new GuiControl(SPB_Backdrop) { profile="SPB_PanelProfile"; };

    // TAB BUTTONS
    new GuiButtonCtrl(SPB_Tab_List) { profile="GuiButtonProfile"; text="List";        command="SPB_UI_ShowTab(\"list\");"; };
    new GuiButtonCtrl(SPB_Tab_Give) { profile="GuiButtonProfile"; text="Give Bounty"; command="SPB_UI_ShowTab(\"give\");"; };
    new GuiButtonCtrl(SPB_Tab_My)   { profile="GuiButtonProfile"; text="My Payout";   command="SPB_UI_ShowTab(\"my\");"; };

    // TABS CONTAINER
    new GuiControl(SPB_Tabs)
    {
      profile="GuiDefaultProfile";

      // ---- LIST TAB ----
      new GuiControl(SPB_P_List) { profile="GuiDefaultProfile"; visible="1";
        new GuiControl(SPB_ListHeader) { profile="SPB_HeaderProfile";
          new GuiTextCtrl(SPB_H_First) { profile="SPB_HeaderProfile"; text="Name"; };
          new GuiTextCtrl(SPB_H_Last)  { profile="SPB_HeaderProfile"; text="L.Name:"; };
          new GuiTextCtrl(SPB_H_Bnty)  { profile="SPB_HeaderProfile"; text="Bounty (c)"; };
        };
        new GuiScrollCtrl(SPB_ListScroll) { profile="GuiScrollProfile"; hScrollBar="alwaysOff"; vScrollBar="dynamic";
          new GuiTextListCtrl(SPB_ListTable)
          {
            profile="SPB_ListProfile";
            // columns: First | Last | Bounty
            columns = "0 260 520 760";
            fitParentWidth = "1";
            clipColumnText = "1";
          };
        };
      };

      // ---- GIVE TAB ----
      new GuiControl(SPB_P_Give) { profile="GuiDefaultProfile"; visible="0";
        new GuiTextCtrl(SPB_Give_Help) { profile="SPB_InfoProfile"; text="Enter target Name/L.Name and copper amount."; };
        new GuiTextCtrl(SPB_Give_L1)   { profile="GuiTextProfile";  text="Name:"; };
        new GuiTextEditCtrl(SPB_Give_First) { profile="GuiTextEditProfile"; };
        new GuiTextCtrl(SPB_Give_L2)   { profile="GuiTextProfile";  text="L.Name:";  };
        new GuiTextEditCtrl(SPB_Give_Last)  { profile="GuiTextEditProfile"; };
        new GuiTextCtrl(SPB_Give_L3)   { profile="GuiTextProfile";  text="Amount (copper): "; };
        new GuiTextEditCtrl(SPB_Give_Amount){ profile="GuiTextEditProfile"; maxLength=8; };
        new GuiButtonCtrl(SPB_Give_Send) { profile="GuiButtonProfile"; text="Send Bounty"; command="SPB_UI_SendGive();"; };
        new GuiTextCtrl(SPB_Give_Info) { profile="SPB_InfoProfile"; text=""; };
      };

      // ---- MY TAB ----
      new GuiControl(SPB_P_My) { profile="GuiDefaultProfile"; visible="0";
        new GuiTextCtrl(SPB_My_Value) { profile="SPB_InfoProfile"; text="Your payout: 0 c"; };
        new GuiButtonCtrl(SPB_My_Refresh) { profile="GuiButtonProfile"; text="Refresh"; command="SPB_RequestMy();"; };
        new GuiButtonCtrl(SPB_My_Claim)   { profile="GuiButtonProfile"; text="Claim";   command="SPB_RequestPayout();"; };
        new GuiTextCtrl(SPB_My_Info) { profile="SPB_InfoProfile"; text=""; };
      };
    };
  };
};

// ---------- LAYOUT & BACKGROUND ----------
function SPB_InitGUI()
{
  // Window
  %cx = (getWord(Canvas.getExtent(),0) - $SPB_UI::WinW) / 2;
  %cy = (getWord(Canvas.getExtent(),1) - $SPB_UI::WinH) / 2;
  SPB_Win.resize(%cx, %cy, $SPB_UI::WinW, $SPB_UI::WinH);

  // Background slightly smaller so it doesn't bleed out
  // (thicker inset than before)
  %bgX = 14; %bgY = 44; %bgW = $SPB_UI::WinW - 28; %bgH = $SPB_UI::WinH - 70;
  SPB_BG.resize(%bgX, %bgY, %bgW, %bgH);
  SPB_BG.setBitmap($SPB_UI::Background);
  SPB_Backdrop.resize(%bgX, %bgY, %bgW, %bgH);

  // Tabs container
  %tabsW = $SPB_UI::WinW - (2 * $SPB_UI::PadX);
  %tabsH = $SPB_UI::WinH - $SPB_UI::PadY - 12;
  SPB_Tabs.resize($SPB_UI::PadX, $SPB_UI::PadY, %tabsW, %tabsH);

  // Tab buttons
  SPB_Tab_List.resize(12, 34, 80, 24);
  SPB_Tab_Give.resize(96, 34, 110, 24);
  SPB_Tab_My.resize(210, 34, 100, 24);

  // ---- List layout ----
  SPB_P_List.resize(0, 0, %tabsW, %tabsH);
  SPB_ListHeader.resize(0, 0, %tabsW, 28);
  SPB_H_First.resize(8,   4, 240, 20);
  SPB_H_Last.resize( 268, 4, 240, 20);
  SPB_H_Bnty.resize( 528, 4, 220, 20);
  SPB_ListScroll.resize(0, 28, %tabsW, %tabsH-28);
  SPB_ListTable.resize(2, 2, %tabsW-6, %tabsH-6);

  // ---- Give layout ----
  SPB_P_Give.resize(0, 0, %tabsW, %tabsH);
  SPB_Give_Help.resize(6, 6, %tabsW-12, 20);
  SPB_Give_L1.resize(6, 44, 80, 18);
  SPB_Give_First.resize(90, 42, 220, 22);
  SPB_Give_L2.resize(6, 76, 80, 18);
  SPB_Give_Last.resize(90, 74, 220, 22);
  SPB_Give_L3.resize(6, 108, 140, 18);
  SPB_Give_Amount.resize(150, 106, 160, 22);
  SPB_Give_Send.resize(90, 142, 120, 26);
  SPB_Give_Info.resize(6, 178, %tabsW-12, 18);

  // ---- My layout ----
  SPB_P_My.resize(0, 0, %tabsW, %tabsH);
  SPB_My_Value.resize(6, 6, 300, 18);
  SPB_My_Refresh.resize(6, 40, 100, 26);
  SPB_My_Claim.resize(112, 40, 100, 26);
  SPB_My_Info.resize(6, 76, %tabsW-12, 18);
}
SPB_InitGUI();

function SPB_OpenUI()
{
  if (!Canvas.isMember(SPB_Window)) Canvas.pushDialog(SPB_Window);
  SPB_Window.visible = "1";

  SPB_UI_ShowTab("list");
}
function SPB_Close(){ if (Canvas.isMember(SPB_Window)) Canvas.popDialog(SPB_Window);  SPB_Window.visible = "0"; }
function clientCmdSPB_OpenUI(){ SPB_OpenUI(); }

function SPB_UI_ShowTab(%which)
{
  SPB_P_List.setVisible(%which $= "list");
  SPB_P_Give.setVisible(%which $= "give");
  SPB_P_My.setVisible(%which $= "my");

  if (%which $= "list") SPB_RequestList();
  if (%which $= "my")   SPB_RequestMy();
}

function SPB_RequestList()   { commandToServer('SPB_List'); }
function SPB_UI_SendGive()
{
  %f = trim(SPB_Give_First.getText());
  %l = trim(SPB_Give_Last.getText());
  %a = mFloor(getMax(0, mFloatLength(SPB_Give_Amount.getText(), 0)));
  if (%f $= "" || %l $= "" || %a <= 0) { SPB_Give_Info.setText("\c2Enter first, last, and positive amount."); return; }
  SPB_Give_Info.setText("\c3Sending...");
  commandToServer('SPB_Give', %f, %l, %a);
}
function SPB_RequestMy()     { commandToServer('SPB_My'); }

function SPB_RequestPayout()
{
  if ($SPB_UI_PayoutBusy) return;
  $SPB_UI_PayoutBusy = true;
  if (isObject(SPB_My_Claim)) SPB_My_Claim.setActive(0);
  commandToServer('SPB_Payout');
}

function clientCmdSPB_List_Begin(){ if (isObject(SPB_ListTable)) SPB_ListTable.clear(); }
function clientCmdSPB_List_Row(%first, %last, %amount)
{
  if (!isObject(SPB_ListTable)) return;
  %row = %first TAB %last TAB (%amount @ " c");
  SPB_ListTable.addRow(SPB_ListTable.rowCount(), %row);
}
function clientCmdSPB_List_End()
{
  if (isObject(SPB_ListTable) && SPB_ListTable.rowCount()==0)
    SPB_ListTable.addRow(0, "— no active bounties —" TAB "" TAB "");
}

function clientCmdSPB_Give_Result(%ok, %msg)
{
  if (%ok) SPB_Give_Info.setText("\c3OK: " @ %msg);
  else     SPB_Give_Info.setText("\c2Error: " @ %msg);
  if (%ok) SPB_RequestList();
}
function clientCmdSPB_My_Result(%total){ SPB_My_Value.setText("Your payout: " @ %total @ " c"); }

function clientCmdSPB_Payout_Result(%ok, %val)
{
  $SPB_UI_PayoutBusy = false;
  if (isObject(SPB_My_Claim)) SPB_My_Claim.setActive(1);

  if (%ok) SPB_My_Info.setText("\c3Claimed: " @ %val @ " c");
  else     SPB_My_Info.setText("\c2Error: " @ %val);

  SPB_RequestMy();
}