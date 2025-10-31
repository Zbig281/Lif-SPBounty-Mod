$SPBounty::Version     = "v1.6.0";
$SPBounty::CoinTypeID  = 1059;   // Copper coin only
$SPBounty::MaxSteps    = 10000;
$SPBounty::Dbg         = false;

function SPB_log(%m){ if ($SPBounty::Dbg) echo("[SP::BountyMod] " @ %m); }

if (!isObject(SPB_DB))        new ScriptObject(SPB_DB){};
if (!isObject(SP_BountyCB))   new ScriptObject(SP_BountyCB){};
$SPBounty::TablesReady = false;

package SPBountyMod
{
   function SPBountyMod::setup()
   {
      if (isFunction("LiFx::registerMod"))
      {
         LiFx::registerMod("SP::BountyMod", $SPBounty::Version, "server",
            "SP::BountyMod — player bounty system", "Zbig Brodaty");
         SPB_log("Registered with LiFx as 'SP::BountyMod'.");
      }
      else
      {
         SPB_log("LiFx registry not available — running standalone.");
      }

      if (isDefined("$LiFx::hooks::onKillCallbacks"))
      {
         LiFx::registerCallback($LiFx::hooks::onKillCallbacks, onKill, SP_BountyCB);
         SPB_log("Registered LiFx onKill callback.");
      }
      if (isDefined("$LiFx::hooks::onSuicideCallbacks"))
      {
         LiFx::registerCallback($LiFx::hooks::onSuicideCallbacks, onSuicide, SP_BountyCB);
         SPB_log("Registered LiFx onSuicide callback.");
      }
   }

   function serverCmdLocalChatMessage(%sender, %message)
   {
      %clean = trim(strreplace(%message, "\t", " "));
      if (getSubStr(%clean, 0, 1) $= "!")
      {
         if (SPB_HandleBang(%sender, getSubStr(%clean, 1, 4096)))
            return;
      }
      Parent::serverCmdLocalChatMessage(%sender, %message);
   }
};
activatePackage(SPBountyMod);
SPBountyMod::setup();
SPB_log("SP::BountyMod " @ $SPBounty::Version @ " initialized");

function SPB__unlockPayout(%kid){ $SPB_PayoutLock[%kid] = 0; }
function SPB__lockPayout(%kid){
  if ($SPB_PayoutLock[%kid]) return false;
  $SPB_PayoutLock[%kid] = 1;
  schedule(5000, 0, "SPB__unlockPayout", %kid);
  return true;
}


function SPB_say(%client, %text)
{
   if (isFunction("cmChatSendLocalMessageToClient"))
      cmChatSendLocalMessageToClient(%client, "Bounty", "Local", %text);
   else if (isFunction("messageClient"))
      messageClient(%client, "", %text);
   else
      echo("[Bounty] " @ %text);
}

function SPB_Help(%c)
{
   %m = "\c3[SP::BountyMod " @ $SPBounty::Version @ "]\n"
      @ " \c6!Bounty\c7 — show this help\n"
      @ " \c6!BountyGive <FirstName> <LastName> <N>c\c7 — place a bounty and pay N copper (e.g. !BountyGive John Doe 20c)\n"
      @ " \c6!BountyList\c7 — list players who currently have a bounty\n"
      @ " \c6!BountyTake\c7 — claim your accumulated bounty payouts";
   SPB_say(%c, %m);
}

function SPB_GetClientByCharId(%charId)
{
   if (!isObject(ClientGroup)) return 0;
   for (%i = 0; %i < ClientGroup.getCount(); %i++)
   {
      %gc = ClientGroup.getObject(%i);
      if (isObject(%gc) && %gc.getCharacterId() == %charId)
         return %gc;
   }
   return 0;
}

function SPB_EnsureTables()
{
   if ($SPBounty::TablesReady) return true;
   if (!isObject(dbi)) { SPB_log("DBI not ready"); return false; }

   %sql1 = "CREATE TABLE IF NOT EXISTS spb_bounties ("
         @ "CharID INT PRIMARY KEY,"
         @ "Name VARCHAR(64) NOT NULL,"
         @ "LastName VARCHAR(64) NOT NULL,"
         @ "Price INT NOT NULL DEFAULT 0,"
         @ "ts TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP"
         @ ") ENGINE=InnoDB";

   %sql2 = "CREATE TABLE IF NOT EXISTS spb_kills ("
         @ "ID BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,"
         @ "VictimCharID INT NOT NULL,"
         @ "VictimName VARCHAR(64) NOT NULL,"
         @ "VictimLastName VARCHAR(64) NOT NULL,"
         @ "KillerCharID INT NOT NULL,"
         @ "KillerName VARCHAR(64) NOT NULL,"
         @ "KillerLastName VARCHAR(64) NOT NULL,"
         @ "Amount INT NOT NULL DEFAULT 0,"
         @ "ts TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,"
         @ "KEY(VictimCharID), KEY(KillerCharID)) ENGINE=InnoDB";

   %sql3 = "CREATE TABLE IF NOT EXISTS spb_payouts ("
         @ "KillerCharID INT PRIMARY KEY,"
         @ "KillerName VARCHAR(64) NOT NULL,"
         @ "KillerLastName VARCHAR(64) NOT NULL,"
         @ "Amount INT NOT NULL DEFAULT 0,"
         @ "last_claim_ts TIMESTAMP NULL DEFAULT NULL"
         @ ") ENGINE=InnoDB";

   dbi.update(%sql1);
   dbi.update(%sql2);
   dbi.update(%sql3);

   $SPBounty::TablesReady = true;
   SPB_log("Tables ensured");
   return true;
}
function SPB_EnsureTables_Retry()
{
   if (!SPB_EnsureTables())
      schedule(1000, 0, "SPB_EnsureTables_Retry");
}
SPB_EnsureTables_Retry();

function SPB_Pop(%listRef)
{
   %lst = trim(%listRef);
   if (%lst $= "") return "\t";
   %first = getWord(%lst, 0);
   %rest  = getWords(%lst, 1);
   return %first TAB %rest;
}

function SPB_FindCharByName(%first, %last, %cbObjRef, %cbMeth)
{
   %q = "SELECT ID,Name,LastName FROM `character` "
      @ "WHERE Name='" @ %first @ "' AND LastName='" @ %last @ "' LIMIT 1";
   %ctx = new ScriptObject(){ f=%first; l=%last; cbObjRef=%cbObjRef; cbMeth=%cbMeth; };
   dbi.select(%ctx, "cbFindChar", %q);
}
function ScriptObject::cbFindChar(%this, %rs)
{
   %ok=false; %id=0; %n=""; %ln="";
   if (%rs.ok() && %rs.nextRecord())
   {
      %ok=true;
      %id=%rs.getFieldValue("ID");
      %n=%rs.getFieldValue("Name");
      %ln=%rs.getFieldValue("LastName");
   }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

   if (isObject(%this.cbObjRef))
      eval("%this.cbObjRef." @ %this.cbMeth @ "(" @ %ok @ ", " @ %id @ ", \"" @ %n @ "\", \"" @ %ln @ "\");");

   %this.delete();
}

function SPB_PayCopper_Begin(%client, %need, %cbObjRef, %cbMeth)
{
   if (%need <= 0) { if (isObject(%cbObjRef)) eval("%cbObjRef." @ %cbMeth @ "(false, \"Amount<=0\", 0);"); return; }
   %charId = %client.getCharacterId();
   if (%charId $= "" || %charId <= 0) { if (isObject(%cbObjRef)) eval("%cbObjRef." @ %cbMeth @ "(false, \"Missing CharID\", 0);"); return; }

   %sql = "SELECT RootContainerID FROM `character` WHERE ID=" @ %charId @ " LIMIT 1";
   %ctx = new ScriptObject(){ klass="pay"; client=%client; need=%need; cbObjRef=%cbObjRef; cbMeth=%cbMeth; };
   dbi.select(%ctx, "cbPayRoot", %sql);
}
function ScriptObject::cbPayRoot(%this, %rs)
{
   %ok=false; %root=0;
   if (%rs.ok() && %rs.nextRecord()) { %root = %rs.getFieldValue("RootContainerID"); %ok = ("" !$= %root); }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }
   if (!%ok) { if (isObject(%this.cbObjRef)) eval("%this.cbObjRef." @ %this.cbMeth @ "(false, \"Missing RootContainerID\", 0);"); %this.delete(); return; }

   %this.queue=%root; %this.list=""; %this.sum=0; %this.steps=0;
   SPB_PayCopper_Next(%this);
}
function SPB_PayCopper_Next(%ctx)
{
   %ctx.steps++; if (%ctx.steps > $SPBounty::MaxSteps){ if (isObject(%ctx.cbObjRef)) eval("%ctx.cbObjRef." @ %ctx.cbMeth @ "(false, \"Inventory BFS limit\", 0);"); %ctx.delete(); return; }
   %pop = SPB_Pop(%ctx.queue);
   %cid = getField(%pop, 0);
   %ctx.queue = getField(%pop, 1);
   if (%cid $= "") { SPB_PayCopper_Apply(%ctx); return; }

   %q = "SELECT ID,ObjectTypeID,Quantity FROM items WHERE ContainerID=" @ %cid @ " ORDER BY ID ASC";
   %ctx.parent=%cid;
   dbi.select(%ctx, "cbPayScan", %q);
}
function ScriptObject::cbPayScan(%this, %rs)
{
   if (%rs.ok())
   {
      while (%rs.nextRecord())
      {
         %id = %rs.getFieldValue("ID");
         %ot = %rs.getFieldValue("ObjectTypeID");
         %qt = %rs.getFieldValue("Quantity");
         if (%ot == $SPBounty::CoinTypeID && %qt > 0)
         {
            %this.list = trim(%this.list SPC (%id @ ":" @ %qt));
            %this.sum += (%qt + 0);
         }
         %this.queue = trim(%this.queue SPC %id);
      }
   }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }
   SPB_PayCopper_Next(%this);
}
function SPB_PayCopper_Apply(%ctx)
{
   if (%ctx.sum < %ctx.need) { if (isObject(%ctx.cbObjRef)) eval("%ctx.cbObjRef." @ %ctx.cbMeth @ "(false, \"Not enough copper\", " @ %ctx.sum @ ");"); %ctx.delete(); return; }

   %pl = 0;
   if (isObject(%ctx.client.player)) %pl = %ctx.client.player;
   if (!isObject(%pl)) %pl = %ctx.client.getControlObject();
   if (!isObject(%pl)) { if (isObject(%ctx.cbObjRef)) eval("%ctx.cbObjRef." @ %ctx.cbMeth @ "(false, \"Missing PlayerObject\", 0);"); %ctx.delete(); return; }

   %removed = 0;
   %i = 0;
   while (true)
   {
      %tok = getWord(%ctx.list, %i);
      if (%tok $= "") break;
      %i++;
      %sep = strstr(%tok, ":");
      %itemId = getSubStr(%tok, 0, %sep);
      %qty    = getSubStr(%tok, %sep+1, 1024);
      %pl.inventoryRemoveItem(%itemId);
      %removed += (%qty + 0);
   }

   %change = %removed - %ctx.need;
   if (%change > 0)
      %pl.inventoryAddItem($SPBounty::CoinTypeID, %change, 50, 0, 0);

   if (isObject(%ctx.cbObjRef))
      eval("%ctx.cbObjRef." @ %ctx.cbMeth @ "(true, \"OK\", " @ %ctx.need @ ");");

   %ctx.delete();
}

function SPB_Cmd_BountyGive(%client, %rest)
{
   if (!SPB_EnsureTables()) { SPB_say(%client, "\c2Database is not ready yet."); return true; }

   %first = getWord(%rest, 0);
   %last  = getWord(%rest, 1);
   %amt   = getWord(%rest, 2);

   if (%first $= "" || %last $= "" || %amt $= "")
   {
      SPB_say(%client, "\c2Usage: !BountyGive <FirstName> <LastName> <N>c  (e.g. !BountyGive John Doe 20c)");
      return true;
   }

   %a = strlwr(%amt); %n = strlen(%a);
   if (%n < 2 || getSubStr(%a, %n-1, 1) !$= "c") { SPB_say(%client, "\c2Amount must be like Nc (e.g. 20c)."); return true; }
   %num = getSubStr(%a, 0, %n-1);
   %ok = true;
   for (%i = 0; %i < strlen(%num); %i++)
   {
   %ch = getSubStr(%num, %i, 1);
   if (strpos("0123456789", %ch) == -1) { %ok = false; break; }
   } 
   if (!%ok) { SPB_say(%client, "\c2Amount must be numeric (e.g. 20c)."); return true; }
   %need = %num + 0; if (%need <= 0) { SPB_say(%client, "\c2Amount must be > 0."); return true; }

   SPB_DB._give_client = %client;
   SPB_DB._give_need   = %need;
   SPB_FindCharByName(%first, %last, SPB_DB, "cbGive_GotTarget");
   return true;
}
function SPB_DB::cbGive_GotTarget(%this, %ok, %targetId, %tName, %tLast)
{
   %client = %this._give_client;
   if (!%ok || %targetId <= 0) { SPB_say(%client, "\c2Target not found in character table."); return; }

   %this._give_tid  = %targetId;
   %this._give_tn   = %tName;
   %this._give_tln  = %tLast;

   SPB_PayCopper_Begin(%client, %this._give_need, SPB_DB, "cbGive_Paid");
}
function SPB_DB::cbGive_Paid(%this, %ok, %msg, %paid)
{
   %client = %this._give_client;
   if (!%ok) { SPB_say(%client, "\c2Bounty payment failed: " @ %msg); return; }

	%q = "INSERT INTO spb_bounties (CharID,Name,LastName,Price) "
	   @ "SELECT c.ID, c.Name, c.LastName, " @ %paid @ " FROM `character` c WHERE c.ID=" @ %this._give_tid @ " "
	   @ "ON DUPLICATE KEY UPDATE "
	   @ "  Name=VALUES(Name), "
	   @ "  LastName=VALUES(LastName), "
	   @ "  Price=Price+VALUES(Price)";
	dbi.update(%q);

   SPB_say(%client, "\c3[Bounty] Added " @ %paid @ "c on \c6" @ %this._give_tn SPC %this._give_tln @ "\c3.");
}

// !BountyList
function SPB_Cmd_BountyList(%client)
{
   if (!SPB_EnsureTables()) { SPB_say(%client, "\c2Database is not ready yet."); return true; }
   %q = "SELECT Name,LastName,Price FROM spb_bounties WHERE Price>0 ORDER BY Price DESC, Name ASC LIMIT 100";
   SPB_DB._list_client = %client;
   dbi.select(SPB_DB, "cbListOut", %q);
   return true;
}
function SPB_DB::cbListOut(%this, %rs)
{
   %client = %this._list_client;
   %out = "\c3[Bounty] Current bounties:\n";
   %any=false;
   if (%rs.ok())
   {
      while (%rs.nextRecord())
      {
         %any=true;
         %n = %rs.getFieldValue("Name");
         %l = %rs.getFieldValue("LastName");
         %p = %rs.getFieldValue("Price")+0;
         %out = %out @ "  \c6" @ %n SPC %l @ "\c7 — " @ %p @ "c\n";
      }
   }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }
   if (!%any) %out = "\c3[Bounty] None.";
   SPB_say(%client, %out);
}

// !BountyTake — claim killer's payout pool
function SPB_Cmd_BountyTake(%client)
{
   if (!SPB_EnsureTables()) { SPB_say(%client, "\c2Database is not ready yet."); return true; }
   %kid = %client.getCharacterId();
   if (!SPB__lockPayout(%kid)) { SPB_say(%client, "\c2[Bounty] Payout is already in progress. Please wait..."); return true; }

   %q = "SELECT Amount FROM spb_payouts WHERE KillerCharID=" @ %kid @ " LIMIT 1";
   SPB_DB._take_client = %client;
   SPB_DB._take_kid    = %kid;
   dbi.select(SPB_DB, "cbTake_Load", %q);
}

function SPB_DB::cbTake_Load(%this, %rs)
{
   %client = %this._take_client; %kid = %this._take_kid;
   %amt=0; if (%rs.ok() && %rs.nextRecord()) %amt = %rs.getFieldValue("Amount")+0;
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

   if (%amt <= 0) { SPB_say(%client, "\c2[Bounty] You have nothing to claim."); SPB__unlockPayout(%kid); return; }

   %upd = "UPDATE spb_payouts SET Amount=0, last_claim_ts=CURRENT_TIMESTAMP "
        @ "WHERE KillerCharID=" @ %kid @ " AND Amount=" @ %amt @ " LIMIT 1";
   dbi.update(%upd);

   %q2 = "SELECT Amount FROM spb_payouts WHERE KillerCharID=" @ %kid @ " LIMIT 1";
   %this._take_amt = %amt;
   dbi.select(%this, "cbTake_AfterZero", %q2);
}

function SPB_DB::cbTake_AfterZero(%this, %rs)
{
   %client = %this._take_client; %kid = %this._take_kid;
   %now=0; if (%rs.ok() && %rs.nextRecord()) %now = %rs.getFieldValue("Amount")+0;
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

   if (%now != 0) { SPB_say(%client, "\c2[Bounty] Payout changed. Try again."); SPB__unlockPayout(%kid); return; }

   %amt = %this._take_amt + 0;
   %pl = 0; if (isObject(%client.player)) %pl = %client.player; if (!isObject(%pl)) %pl = %client.getControlObject();
   if (!isObject(%pl)) {

      %restore = "UPDATE spb_payouts SET Amount=Amount+" @ %amt @ " WHERE KillerCharID=" @ %kid @ " LIMIT 1";
      dbi.update(%restore);
      SPB_say(%client, "\c2Missing PlayerObject (cannot deliver payout).");
      SPB__unlockPayout(%kid);
      return;
   }

   %pl.inventoryAddItem($SPBounty::CoinTypeID, %amt, 50, 0, 0);
   SPB_say(%client, "\c3[Bounty] Claimed: " @ %amt @ "c.");
   SPB__unlockPayout(%kid);
}

function SP_BountyCB::onKill(%this, %CharID, %KillerID, %isKnockout, %Tombstone)
{
   if (%isKnockout) return;
   if (!SPB_EnsureTables()) return;

   %qB = "SELECT b.Price, c.Name, c.LastName, c.GuildID AS VictimGuildID "
      @ "FROM spb_bounties b JOIN `character` c ON c.ID=b.CharID "
      @ "WHERE b.CharID=" @ %CharID @ " LIMIT 1";
   %ctx = new ScriptObject(){ kid=%KillerID; vid=%CharID; };
   dbi.select(%ctx, "cbKill_LoadVictimBounty", %qB);
}

function ScriptObject::cbKill_LoadVictimBounty(%this, %rs)
{
   %price=0; %vn=""; %vl=""; %vgid=0;
   if (%rs.ok() && %rs.nextRecord())
   {
      %price = %rs.getFieldValue("Price") + 0;
      %vn    = %rs.getFieldValue("Name");
      %vl    = %rs.getFieldValue("LastName");
      %vgid  = %rs.getFieldValue("VictimGuildID") + 0;
   }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

   if (%price <= 0) { %this.delete(); return; }

   %this._kill_amount = %price;
   %this._victimN     = %vn;
   %this._victimL     = %vl;
   %this._victimGuild = %vgid;

   %qK = "SELECT Name,LastName,GuildID FROM `character` WHERE ID=" @ %this.kid @ " LIMIT 1";
   dbi.select(%this, "cbKill_LoadKillerName", %qK);
}

function ScriptObject::cbKill_LoadKillerName(%this, %rs)
{
   %kn=""; %kl=""; %kgid=0;
   if (%rs.ok() && %rs.nextRecord())
   {
      %kn   = %rs.getFieldValue("Name");
      %kl   = %rs.getFieldValue("LastName");
      %kgid = %rs.getFieldValue("GuildID") + 0;
   }
   if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

   %price = %this._kill_amount;
   %vn = %this._victimN; %vl = %this._victimL;
   %vgid = %this._victimGuild + 0;

   if (%vgid > 0 && %kgid > 0 && %vgid == %kgid)
   {
      %insK = "INSERT INTO spb_kills (VictimCharID,VictimName,VictimLastName,"
           @ "KillerCharID,KillerName,KillerLastName,Amount) VALUES ("
           @ %this.vid @ ",'" @ %vn @ "','" @ %vl @ "',"
           @ %this.kid @ ",'" @ %kn @ "','" @ %kl @ "',0)";
      dbi.update(%insK);

      %kc = SPB_GetClientByCharId(%this.kid);
      if (isObject(%kc))
         SPB_say(%kc, "\c2[Bounty] No payout: the victim and the killer belong to the same guild.");

      %this.delete();
      return;
   }

   %insK = "INSERT INTO spb_kills (VictimCharID,VictimName,VictimLastName,"
        @ "KillerCharID,KillerName,KillerLastName,Amount) VALUES ("
        @ %this.vid @ ",'" @ %vn @ "','" @ %vl @ "',"
        @ %this.kid @ ",'" @ %kn @ "','" @ %kl @ "'," @ %price @ ")";
   dbi.update(%insK);

   %insP = "INSERT INTO spb_payouts (KillerCharID,KillerName,KillerLastName,Amount) VALUES ("
        @ %this.kid @ ",'" @ %kn @ "','" @ %kl @ "'," @ %price @ ") "
        @ "ON DUPLICATE KEY UPDATE KillerName=VALUES(KillerName), "
        @ "KillerLastName=VALUES(KillerLastName), Amount=Amount+" @ %price;
   dbi.update(%insP);

   %updB = "UPDATE spb_bounties SET Price=0, ts=CURRENT_TIMESTAMP WHERE CharID="
        @ %this.vid @ " LIMIT 1";
   dbi.update(%updB);

   %kc = SPB_GetClientByCharId(%this.kid);
   if (isObject(%kc))
      SPB_say(%kc, "\c3[Bounty] You killed \c6" @ %vn SPC %vl @ "\c3 — added \c6" @ %price @ "c\c3 to your payout. Use \c6!BountyTake");

   %this.delete();
}

function SP_BountyCB::onSuicide(%this, %CharID, %isKnockout, %Tombstone)
{   
}

function SPB_HandleBang(%client, %raw)
{
   %cmd  = strlwr(firstWord(%raw));
   %rest = trim(getSubStr(%raw, strlen(%cmd), 4096));

   if (%cmd $= "bounty")       { SPB_Help(%client); return true; }
   if (%cmd $= "bountygive")   { return SPB_Cmd_BountyGive(%client, %rest); }
   if (%cmd $= "bountylist")   { return SPB_Cmd_BountyList(%client); }
   if (%cmd $= "bountytake")   { return SPB_Cmd_BountyTake(%client); }

   if (getSubStr(%cmd,0,6) $= "bounty") { SPB_Help(%client); return true; }
   return false;
}

package SPBounty_GUI_Bridge
{
  function SPB_HandleBang(%client, %raw)
  {
    %cmd  = strlwr(firstWord(%raw));
    if (%cmd $= "bounty")
    {
      if (isObject(%client))
        commandToClient(%client, 'SPB_OpenUI');
      return true;
    }
    return Parent::SPB_HandleBang(%client, %raw);
  }
};
activatePackage(SPBounty_GUI_Bridge);

function serverCmdSPB_List(%client)
{
  if (!SPB_EnsureTables()) { commandToClient(%client, 'SPB_List_Begin'); commandToClient(%client, 'SPB_List_End'); return; }
  %q = "SELECT Name,LastName,Price FROM spb_bounties WHERE Price>0 ORDER BY Price DESC, Name ASC LIMIT 100";
  %ctx = new ScriptObject(){ c = %client; };
  dbi.select(%ctx, "SPB__cbUI_List", %q);
}
function ScriptObject::SPB__cbUI_List(%this, %rs)
{
  %c = %this.c;
  commandToClient(%c, 'SPB_List_Begin');
  if (%rs.ok())
  {
    while (%rs.nextRecord())
    {
      %n = %rs.getFieldValue("Name");
      %l = %rs.getFieldValue("LastName");
      %p = %rs.getFieldValue("Price")+0;
      commandToClient(%c, 'SPB_List_Row', %n, %l, %p);
    }
  }
  commandToClient(%c, 'SPB_List_End');
  if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }
  %this.delete();
}

function serverCmdSPB_Give(%client, %first, %last, %amountC)
{
  if (!SPB_EnsureTables()) { commandToClient(%client, 'SPB_Give_Result', false, "DB not ready"); return; }
  %need = (%amountC+0);
  if (%first $= "" || %last $= "" || %need <= 0)
  {
    commandToClient(%client, 'SPB_Give_Result', false, "Invalid input");
    return;
  }

  SPB_DB._ui_client = %client;
  SPB_DB._ui_need   = %need;
  SPB_FindCharByName(%first, %last, SPB_DB, "cbUI_Give_GotTarget");
}
function SPB_DB::cbUI_Give_GotTarget(%this, %ok, %targetId, %tName, %tLast)
{
  %client = %this._ui_client;
  if (!%ok || %targetId <= 0) { commandToClient(%client, 'SPB_Give_Result', false, "Target not found"); return; }
  %this._ui_tid  = %targetId;
  %this._ui_tn   = %tName;
  %this._ui_tln  = %tLast;
  SPB_PayCopper_Begin(%client, %this._ui_need, SPB_DB, "cbUI_Give_Paid");
}
function SPB_DB::cbUI_Give_Paid(%this, %ok, %msg, %paid)
{
  %client = %this._ui_client;
  if (!%ok) { commandToClient(%client, 'SPB_Give_Result', false, %msg); return; }

	%q = "INSERT INTO spb_bounties (CharID,Name,LastName,Price) "
	   @ "SELECT c.ID, c.Name, c.LastName, " @ %paid @ " FROM `character` c WHERE c.ID=" @ %this._ui_tid @ " "
	   @ "ON DUPLICATE KEY UPDATE "
	   @ "  Name=VALUES(Name), "
	   @ "  LastName=VALUES(LastName), "
	   @ "  Price=Price+VALUES(Price)";
	dbi.update(%q);

  commandToClient(%client, 'SPB_Give_Result', true, "Added " @ %paid @ "c on " @ %this._ui_tn SPC %this._ui_tln);
}

function serverCmdSPB_My(%client)
{
  if (!SPB_EnsureTables()) { commandToClient(%client, 'SPB_My_Result', 0); return; }
  %kid = %client.getCharacterId();
  %q = "SELECT Amount FROM spb_payouts WHERE KillerCharID=" @ %kid @ " LIMIT 1";
  %ctx = new ScriptObject(){ c=%client; };
  dbi.select(%ctx, "SPB__cbUI_My", %q);
}
function ScriptObject::SPB__cbUI_My(%this, %rs)
{
  %amt=0; if (%rs.ok() && %rs.nextRecord()) %amt = %rs.getFieldValue("Amount")+0;
  if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }
  commandToClient(%this.c, 'SPB_My_Result', %amt);
  %this.delete();
}

function serverCmdSPB_Payout(%client)
{
  if (!SPB_EnsureTables()) { commandToClient(%client, 'SPB_Payout_Result', false, "DB not ready"); return; }
  %kid = %client.getCharacterId();
  if (!SPB__lockPayout(%kid)) { commandToClient(%client, 'SPB_Payout_Result', false, "Payout already in progress"); return; }

  %q = "SELECT Amount FROM spb_payouts WHERE KillerCharID=" @ %kid @ " LIMIT 1";
  %ctx = new ScriptObject(){ c=%client; kid=%kid; };
  dbi.select(%ctx, "SPB__cbUI_Payout_Load", %q);
}

function ScriptObject::SPB__cbUI_Payout_Load(%this, %rs)
{
  %client = %this.c; %kid = %this.kid; %amt=0;
  if (%rs.ok() && %rs.nextRecord()) %amt = %rs.getFieldValue("Amount")+0;
  if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

  if (%amt <= 0) { commandToClient(%client, 'SPB_Payout_Result', false, "Nothing to claim"); SPB__unlockPayout(%kid); %this.delete(); return; }

  %upd = "UPDATE spb_payouts SET Amount=0, last_claim_ts=CURRENT_TIMESTAMP "
       @ "WHERE KillerCharID=" @ %kid @ " AND Amount=" @ %amt @ " LIMIT 1";
  dbi.update(%upd);

  %q2 = "SELECT Amount FROM spb_payouts WHERE KillerCharID=" @ %kid @ " LIMIT 1";
  %this._ui_amt = %amt;
  dbi.select(%this, "SPB__cbUI_Payout_AfterZero", %q2);
}

function ScriptObject::SPB__cbUI_Payout_AfterZero(%this, %rs)
{
  %client = %this.c; %kid = %this.kid;
  %now=0; if (%rs.ok() && %rs.nextRecord()) %now = %rs.getFieldValue("Amount")+0;
  if (isObject(%rs)) { dbi.remove(%rs); %rs.delete(); }

  if (%now != 0) { commandToClient(%client, 'SPB_Payout_Result', false, "Payout changed. Try again."); SPB__unlockPayout(%kid); %this.delete(); return; }

  %amt = %this._ui_amt + 0;

  %pl = 0; if (isObject(%client.player)) %pl = %client.player; if (!isObject(%pl)) %pl = %client.getControlObject();
  if (!isObject(%pl)) {
    %restore = "UPDATE spb_payouts SET Amount=Amount+" @ %amt @ " WHERE KillerCharID=" @ %kid @ " LIMIT 1";
    dbi.update(%restore);
    commandToClient(%client, 'SPB_Payout_Result', false, "No player");
    SPB__unlockPayout(%kid);
    %this.delete(); return;
  }

  if ($SPBounty::CoinTypeID $= "") $SPBounty::CoinTypeID = 1059;
  %pl.inventoryAddItem($SPBounty::CoinTypeID, %amt, 50, 0, 0);

  commandToClient(%client, 'SPB_Payout_Result', true, %amt);
  SPB__unlockPayout(%kid);
  %this.delete();
}
