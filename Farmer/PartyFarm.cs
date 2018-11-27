using Out.Internal.Core;
using Out.Utility;
using PartyFarm.ClassConfigs;
using PartyFarm.FarmConfig;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm
{
    enum EMoveReason
    {
        None,
        ComeToRepairman,
        ComeToRepairPoint,
        MoveToBestFarmPos,
        PartyHeal,
        UseTotem,
        BackToFarmZone,
        ComeToMobForPull,
        ComeToMobForAttack,
        CollectLoot,
        ComeToResPlayer,
        ComeToSafepoint,
        ComeToFarmStartPoint,
        ComeToServerPathPoint
    }
    [Flags]
    enum EFarmState
    {
        Farming = 1,
        LocalRepair = 2,
        LocalSell = 4,
        GlobalAuction = 8,
        NeedMoveToSafeZone = 16,
        NeedCallRepairman = 32,
        LocalEquip = 64,
        DontPull = 128,


        LocalAction = LocalRepair | LocalSell | LocalEquip | NeedCallRepairman
    }
    public class PartyFarm : Core
    {
        public bool isReleaseVersion = true;
        static uint BlackOxTotem = 61146;
        static double MaxInteractionDistance = 5f;

        string NeedRepairMessage = "NeedRepair";
        string RepairDoneMessage = "RepairDone";
        string NeedSellMessage = "NeedSell";
        string SellDoneMessage = "SellDone";
        string DontPullMessage = "NeedStopPull";
        string CanPullMessage = "CanPullAgain";

        double PullSpellRange = 30f;
        EMoveReason MoveReason = EMoveReason.None;
        EFarmState State = 0;
        bool IsPuller; //пулеры не нужно возвращаться в зону фарма по кд, он туда возвращается когда нечего пулить или напулил.
        bool CanWork;
        FarmConfigDefault FarmCfg;
        ClassConfig ClassCfg;


        Unit Totem;
        Unit BestMob;
        bool BestMobInsideFZ = false;
        List<Unit> mobsCanBePulled = new List<Unit>();
        List<Unit> aggroMobsAll = new List<Unit>();
        List<Unit> aggroMobsOnParty = new List<Unit>();
        List<Unit> aggroMobsOnMe = new List<Unit>();
        List<Unit> aggroMobsInsideFarmZone = new List<Unit>();

        List<Unit> GetMobsCanBePulled()
        {
            lock (VarLocker)
                return mobsCanBePulled.ToList();
        }
        List<Unit> GetAggroMobsAll()
        {
            lock (VarLocker)
                return aggroMobsAll.ToList();
        }
        List<Unit> GetAggroMobsOnParty()
        {
            lock (VarLocker)
                return aggroMobsOnParty.ToList();
        }
        List<Unit> GetAggroMobsOnMe()
        {
            lock (VarLocker)
                return aggroMobsOnMe.ToList();
        }
        List<Unit> GetAggroMobsInsideFarmZone()
        {
            lock (VarLocker)
                return aggroMobsInsideFarmZone.ToList();
        }

        int GetMobsCanBePulledCount()
        {
            lock (VarLocker)
                return mobsCanBePulled.Count;
        }
        int GetAggroMobsAllCount()
        {
            lock (VarLocker)
                return aggroMobsAll.Count;
        }
        int GetAggroMobsOnPartyCount()
        {
            lock (VarLocker)
                return aggroMobsOnParty.Count;
        }
        int GetAggroMobsOnMeCount()
        {
            lock (VarLocker)
                return aggroMobsOnMe.Count;
        }
        int GetAggroMobsInsideFarmZoneCount()
        {
            lock (VarLocker)
                return aggroMobsInsideFarmZone.Count;
        }

        Random RandomGen = new Random((int)DateTime.UtcNow.Ticks);
        DateTime NextDeleteTrashItem = DateTime.MinValue;
        DateTime NextDisenchant = DateTime.MinValue;
        DateTime NextCheckDist = DateTime.MinValue; 
        DateTime NextPickup = DateTime.MinValue;
        DateTime NextRepairOrSellMessageAllow = DateTime.MinValue;
        DateTime NextRepairOrSellAllow = DateTime.MinValue;
        DateTime NextRandomMoveAllow = DateTime.UtcNow;
        DateTime NextBestPosMoveAllow = DateTime.UtcNow;
        DateTime NextRandomMovesDirChange = DateTime.UtcNow;
        DateTime NextSpellResTry = DateTime.MinValue;


        void SendDontPull()
        {
            foreach (var g in Group.GetMembers())
            {
                var obj = GetEntity(g.Guid) as Player;
                if (obj != null)
                    SendMessageToWoWCharacter(obj.ServerName, obj.Name, DontPullMessage);
            }
        }
        void SendCanPull()
        {
            foreach (var g in Group.GetMembers())
            {
                var obj = GetEntity(g.Guid) as Player;
                if (obj != null)
                    SendMessageToWoWCharacter(obj.ServerName, obj.Name, CanPullMessage);
            }
        }
        bool IsWithinFarmZone()
        {
            return Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) < 150;
        }
        void UpdateFarmState()
        {
            bool haveActiveRepairOrSellRequests = false;
            bool haveActiveDpntPullRequests = false;


            //действия ниже нельзя делать если мы труп
            if (IsDead)
                return;

            if ((State & EFarmState.DontPull) != 0)
            {
                foreach (var r in DontPullRequests)
                {
                    if (r.Value > DateTime.UtcNow)
                        haveActiveDpntPullRequests = true;
                }
                if (!haveActiveDpntPullRequests)
                    State &= ~EFarmState.DontPull;
            }
            if ((State & EFarmState.NeedCallRepairman) != 0)
            {
                foreach (var r in RepairRequests)
                {
                    if (r.Value > DateTime.UtcNow)
                        haveActiveRepairOrSellRequests = true;
                }
                foreach (var r in SellRequests)
                {
                    if (r.Value > DateTime.UtcNow)
                        haveActiveRepairOrSellRequests = true;
                }
                
                if (!haveActiveRepairOrSellRequests)
                    State &= ~EFarmState.NeedCallRepairman;
            }

            

            
            bool needRepair = false;
            foreach (var i in ItemManager.GetItems())
            {
                if (i.Place == EItemPlace.Equipment && i.MaxDurability != 0 && i.Durability == 0)
                {
                    needRepair = true;
                    break;
                }
            }

            if (needRepair && !string.IsNullOrEmpty(FarmCfg.Repairman))
            {
                State |= EFarmState.LocalRepair;
                if (NextRepairOrSellMessageAllow < DateTime.UtcNow)
                {
                    NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                    if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0)
                        SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, NeedRepairMessage);
                    foreach (var p in GetEntities<Player>())
                    {
                        if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0)
                            SendMessageToWoWCharacter(p.ServerName, p.Name, NeedRepairMessage);
                    }
                    SendDontPull();
                }
            }
            else
            {
                bool changed = false;
                if ((State & EFarmState.LocalRepair) != 0)
                    changed = true;
                State &= ~EFarmState.LocalRepair;
                if (changed && (State & EFarmState.LocalAction) == 0)
                    SendCanPull();
            }




            bool needSell = ItemManager.GetFreeInventorySlotsCount() < 10;
            if (needSell && !string.IsNullOrEmpty(FarmCfg.Repairman))
            {
                State |= EFarmState.LocalSell;
                if (NextRepairOrSellMessageAllow < DateTime.UtcNow)
                {
                    NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                    if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0)
                        SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, NeedSellMessage);
                    foreach (var p in GetEntities<Player>())
                    {
                        if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0)
                            SendMessageToWoWCharacter(p.ServerName, p.Name, NeedSellMessage);
                    }
                    SendDontPull();
                }
            }
            else
            {
                bool changed = false;
                if ((State & EFarmState.LocalSell) != 0)
                    changed = true;
                State &= ~EFarmState.LocalSell;
                if (changed && (State & EFarmState.LocalAction) == 0)
                    SendCanPull();
            }


            var toEquip = EquipBestArmorAndWeapon();
            bool needEquip = toEquip != null && toEquip.Count > 0;
            if (FarmCfg.LogsEnabled && needEquip)
                foreach (var e in toEquip)
                    Console.WriteLine("CAN EQUIP: " + e.Name + "[" + e.Level + "]");
            if (needEquip)
            {
                if ((State & EFarmState.LocalEquip) == 0)
                    SendDontPull();
                State |= EFarmState.LocalEquip;
            }
            else
            {
                bool changed = false;
                if ((State & EFarmState.LocalEquip) != 0)
                    changed = true;
                State &= ~EFarmState.LocalEquip;
                if (changed && (State & EFarmState.LocalAction) == 0)
                    SendCanPull();
            }

        }
        Dictionary<string, DateTime> DontPullRequests = new Dictionary<string, DateTime>();
        Dictionary<string, DateTime> RepairRequests = new Dictionary<string, DateTime>();
        Dictionary<string, DateTime> SellRequests = new Dictionary<string, DateTime>();
        void PartyFarm_onCharacterMessage(string SenderServerName, string SenderName, string Message)
        {
            Log(SenderName + "-" + SenderServerName + ": " + Message, Me.Name);
            if (Message == NeedRepairMessage)
            {
                RepairRequests[SenderName + "-" + SenderServerName] = DateTime.UtcNow.AddMinutes(1);
                State |= EFarmState.NeedCallRepairman;
            }
            if (Message == NeedSellMessage)
            {
                SellRequests[SenderName + "-" + SenderServerName] = DateTime.UtcNow.AddMinutes(1);
                State |= EFarmState.NeedCallRepairman;
            }
            if (Message == RepairDoneMessage)
                RepairRequests[SenderName + "-" + SenderServerName] = DateTime.MinValue;
            if (Message == SellDoneMessage)
                SellRequests[SenderName + "-" + SenderServerName] = DateTime.MinValue;

            if (Message == DontPullMessage)
            {
                DontPullRequests[SenderName + "-" + SenderServerName] = DateTime.UtcNow.AddMinutes(1);
                State |= EFarmState.DontPull;
            }
            if (Message == CanPullMessage)
                DontPullRequests[SenderName + "-" + SenderServerName] = DateTime.MinValue;
        }
        void SellAllTrash()
        {
            int q = 0;
            foreach (var i in ItemManager.GetItems())
            {
                if (i.Place >= EItemPlace.InventoryBag && i.Place <= EItemPlace.Bag4)
                {
                    if ((i.ItemQuality == EItemQuality.Poor)
                        || ((i.ItemQuality == EItemQuality.Normal || i.ItemQuality == EItemQuality.Uncommon) && (i.ItemClass == EItemClass.Armor || i.ItemClass == EItemClass.Weapon)))
                    {
                        if (q > 10)
                        {
                            Thread.Sleep(2500);
                            q = 0;
                        }
                        var x = i.Sell(false);
                        Log("Try to sell " + i.Name + " with result: " + x + "[" + GetLastError() + "]", Me.Name);
                        Thread.Sleep(RandomGen.Next(155, 252));
                        q++;
                    }
                }
            }
        }

        void CommonActions()
        {
            if (State == EFarmState.NeedMoveToSafeZone && (MoveReason == EMoveReason.ComeToSafepoint || MoveReason == EMoveReason.ComeToServerPathPoint))
            {
                if (BestMob != null && (Me.IsMoving || MoveQueue.Count != 0))
                    CancelMovesAndWaitStop();
            }
            if (State == EFarmState.Farming)
            {
                //в процессе бега проверяем что можно продолжать пулить
                if (MoveReason == EMoveReason.ComeToMobForPull && !CanContinuePull())
                    CancelMovesAndWaitStop();

                if (MoveReason == EMoveReason.ComeToMobForPull || MoveReason == EMoveReason.BackToFarmZone)
                {
                    //пока бежим - проверяем может кого то еще запулить можем
                    if (ClassCfg.PullSpellId != 0)
                    {
                        foreach (var mob in GetMobsCanBePulled())
                        {
                            if (PullSpellRange == 0 || (PullSpellRange != 0 && PullSpellRange > Me.Distance(mob)))
                            {
                                if (UseSingleSpell(ClassCfg.PullSpellId, false, mob) == ESpellCastError.SUCCESS)
                                    break;
                            }
                        }
                    }
                }

                if (BestMob != null)
                {
                    foreach (var spell in ClassCfg.AttackSpellIds)
                    {
                        if (!spell.IsInstaForAoeFarm)
                            continue;
                        var spellInstant = IsSpellInstant(spell.Id);
                        var spellCastRange = Math.Max(0, GetSpellCastRange(spell.Id) - 1);
                        if (spellCastRange != 0 && spellCastRange > Me.Distance(BestMob))
                            continue;
                        if (UseSingleSpell(spell.Id, false, BestMob, spell.SendLocation ? BestMob.Location : new Vector3F(), false) == ESpellCastError.SUCCESS)
                            return;
                    }
                }
            }
        }
        bool IsDead
        {
            get
            {
                if (Me.IsAlive && Me.IsDeadGhost)
                    return true;
                else if (!Me.IsAlive)
                    return true;
                return false;
            }
        }
        Vector3F SafeRegroupPointRandomized;
        void Route()
        {
            try
            {
                if (IsDead)
                {
                    if (ResurrecterGuid != WowGuid.Zero)
                    {
                        foreach (var g in Group.GetMembers())
                        {
                            if (g.Guid == ResurrecterGuid)
                                AcceptRessurect();
                        }
                    }
                    return;
                }

                if ((State & EFarmState.NeedCallRepairman) != 0 && FarmCfg.RepairmanMountSpellId != 0)
                {
                    if (FarmCfg.RepairSummonPoint != Vector3F.Zero && Me.Distance(FarmCfg.RepairSummonPoint) > 3)
                        ComeToAndWaitStop(FarmCfg.RepairSummonPoint, 1, EMoveReason.ComeToRepairPoint);
                    Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor && npc.IsArmorer);
                    if (!Me.IsInCombat && npcRepairman == null)
                        SpellManager.CastSpell(FarmCfg.RepairmanMountSpellId);
                }

                if ((State & EFarmState.LocalRepair) != 0 && NextRepairOrSellAllow < DateTime.UtcNow)
                {
                    Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor && npc.IsArmorer);
                    if (npcRepairman != null)
                    {
                        NextRepairOrSellAllow = DateTime.UtcNow.AddSeconds(30);
                        ComeToAndWaitStop(npcRepairman, 1f, EMoveReason.ComeToRepairman);
                        if (ItemManager.RepairAllItems())
                        {
                            if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0)
                                SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, RepairDoneMessage);
                            foreach (var p in GetEntities<Player>())
                            {
                                if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0)
                                    SendMessageToWoWCharacter(p.ServerName, p.Name, RepairDoneMessage);
                            }
                            State &= ~EFarmState.LocalRepair;
                            if ((State & EFarmState.LocalAction) == 0)
                                SendCanPull();
                        }
                        return;
                    }
                }
                if ((State & EFarmState.LocalSell) != 0 && NextRepairOrSellAllow < DateTime.UtcNow)
                {
                    Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor);
                    if (npcRepairman != null)
                    {
                        NextRepairOrSellAllow = DateTime.UtcNow.AddSeconds(30);
                        ComeToAndWaitStop(npcRepairman, 1f, EMoveReason.ComeToRepairman);
                        SellAllTrash();
                        if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0)
                            SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, SellDoneMessage);
                        foreach (var p in GetEntities<Player>())
                        {
                            if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0)
                                SendMessageToWoWCharacter(p.ServerName, p.Name, SellDoneMessage);
                        }
                        State &= ~EFarmState.LocalSell;
                        if ((State & EFarmState.LocalAction) == 0)
                            SendCanPull();
                        return;
                    }
                }
                if ((State & EFarmState.LocalEquip) != 0)
                {
                    if (FarmCfg.RepairSummonPoint != Vector3F.Zero && Me.Distance(FarmCfg.RepairSummonPoint) > 3)
                        ComeToAndWaitStop(FarmCfg.RepairSummonPoint, 1, EMoveReason.ComeToRepairPoint);
                    if (!Me.IsInCombat)
                    {
                        var toEquip = EquipBestArmorAndWeapon();
                        if (toEquip != null)
                        {
                            foreach (var item in toEquip)
                            {
                                if (item.Equip())
                                    Thread.Sleep(RandomGen.Next(555, 1555));
                            }
                        }
                    }
                }


                //мы не можем фармить, если
                //(State & EFarmState.NeedCallRepairman) != 0 
                //(State & EFarmState.LocalEquip) != 0
                //LocalRepair и LocalSell - требующие нпц - не дойдут сюда, если нпц есть
                if ((State & EFarmState.NeedCallRepairman) != 0 || (State & EFarmState.LocalEquip) != 0)
                    return;




                CommonActions();
                WaitCasts();
                if ((State & EFarmState.NeedMoveToSafeZone) != 0)
                {
                    if (CheckNeedResAnothers())
                        return;
                    if (HealSelf())
                        return;
                    if (HealParty())
                        return;
                    if (BestMob != null)
                    {
                        if (BuffSelf())
                            return;
                        if (UseAttackSpells())
                            return;
                    }
                    else
                    {
                        if (Me.Distance(SafeRegroupPointRandomized) > 10)
                            ComeToAndWaitStop(SafeRegroupPointRandomized, 2.5, EMoveReason.ComeToSafepoint);
                    }
                }
                else if ((State & EFarmState.Farming) != 0)
                {
                    CancelMounts();
                    DeteleTrashItems();

                    if (CheckNeedResAnothers())
                        return;
                    if (CheckShapeshift())
                        return;
                    if (CheckDistToFarmZone())
                        return;
                    if (UseTotem())
                        return;
                    if (HealSelf())
                        return;
                    if (HealParty())
                        return;
                    if (PreventSpellcast())
                        return;
                    if (CollectLoot())
                        return;
                    if (PullMobs())
                        return;
                    if (BuffSelf())
                        return;
                    if (Disenchant())
                        return;
                    if (UseTaunt())
                        return;
                    TryRandomMoves();

                    if (UseAttackSpells())
                        return;
                }
            }
            catch (Exception e)
            {
                    Console.WriteLine(e);
            }
        }

        Vector3F HordeMailbox = new Vector3F(1606.69, -4422.37, 13.73);
        Vector3F HordeAuction = new Vector3F(1639.26, -4443.71, 17.05);
        public void MoveToSafeSpot()
        { }
        public void SellOnAuction()
        {
            uint AllianceAuctionMapId = 0;
            uint HordeAuctionMapId = 1;
            //Me.Team 
        }
        public void PluginRun()
        {
            try
            {
                while (GameState != EGameState.Ingame)
                    Thread.Sleep(1000);
                ClearLogs();
                onMoveTick += PartyFarm_onMoveTick;
                onCharacterMessage += PartyFarm_onCharacterMessage;
                onChatMessage += PartyFarm_onChatMessage;
                CanWork = true;

                var cfgPath = "Configs/" + Me.Name + "_" + CurrentServer.Name + ".txt";
                Log("Loading cfg: " + cfgPath, Me.Name);
                FarmCfg = (FarmConfigDefault)ConfigLoader.LoadConfig(cfgPath, typeof(FarmConfigDefault), new FarmConfigDefault());
                ConfigLoader.SaveConfig(cfgPath, FarmCfg);
                Log(FarmCfg.TotemInstallZone.X + ", " + FarmCfg.TotemInstallZone.Y, Me.Name);
                InitClassCfg();
                FarmZone = new RoundZone(FarmCfg.TotemInstallZone.X, FarmCfg.TotemInstallZone.Y, FarmCfg.FarmZoneRadius);
                SafeZone = new RoundZone(FarmCfg.SafeRegroupPoint.X, FarmCfg.SafeRegroupPoint.Y, 8);
                SafeRegroupPointRandomized = new Vector3F(FarmCfg.SafeRegroupPoint.X + GetRandomNumber(-3,3), FarmCfg.SafeRegroupPoint.Y + GetRandomNumber(-3, 3), FarmCfg.SafeRegroupPoint.Z);
                IsPuller = FarmCfg.PullPolygoneZone != null || FarmCfg.PullRoundZone != null;
                if (ClassCfg.PullSpellId != 0)
                    PullSpellRange = Math.Max(0, GetSpellCastRange(ClassCfg.PullSpellId));

                if (IsWithinFarmZone() && !SafeZone.ObjInZone(Me))
                    State |= EFarmState.Farming;
                else
                    State |= EFarmState.NeedMoveToSafeZone;



                Task.Run(() => MovesTask());
                Task.Run(() => SetupVariables()); 
                while (GameState != EGameState.Offline)
                {
                    while (GameState != EGameState.Ingame)
                        Thread.Sleep(1000);
                    UpdateFarmState();
                    Route();

                    Thread.Sleep(100);
                }
            }
            catch
            (Exception e)
            {
                Log(e.ToString());
            }
            finally
            {
                CanWork = false;
            }
        }

        private void PartyFarm_onChatMessage(EChatMessageType type, string text, string receiver)
        {
            if (type == EChatMessageType.Party || type == EChatMessageType.PartyLeader)
            {
                if (text.ToLower() == "go farm")
                {
                    Thread.Sleep(RandomGen.Next(0, 2000));
                    State &= ~EFarmState.NeedMoveToSafeZone;
                    State |= EFarmState.Farming;
                }
                if (text.ToLower() == "go afk")
                {
                    Thread.Sleep(RandomGen.Next(0, 2000));
                    State &= ~EFarmState.Farming;
                    State |= EFarmState.NeedMoveToSafeZone;
                }
            }
        }

        private void PartyFarm_onMoveTick(Vector3F loc)
        {
            if (GameState == EGameState.Ingame)
            {
                CommonActions();
            }
        }
        void DeteleTrashItems()
        {
            List<uint> AdditionaItemsForDelete = new List<uint>() { 155593, 163580 };
            if (!FarmCfg.DeleteTrashItems)
                return;
            if (NextDeleteTrashItem > DateTime.UtcNow)
                return;
            foreach (var i in ItemManager.GetItems())
            {
                if (AdditionaItemsForDelete.Contains(i.Id))
                {
                    i.Destroy();
                    NextDeleteTrashItem = DateTime.UtcNow.AddMilliseconds(RandomGen.Next(1111, 2222));
                }
                else if (i.ItemQuality == EItemQuality.Poor && i.Place >= EItemPlace.InventoryBag && i.Place <= EItemPlace.Bag4
                    && (i.ItemClass == EItemClass.Armor || i.ItemClass == EItemClass.Weapon))
                {
                    i.Destroy();
                    NextDeleteTrashItem = DateTime.UtcNow.AddMilliseconds(RandomGen.Next(1111, 2222));
                }
            }
        }

        enum EMoveCallCmdType
        {
            MoveTo,
            ComeTo,
            ForceMoveTo,
            ForceComeTo,
            FlyTo,
            MoveWithLookAt
        }
        class MoveRequest
        {
            public EMoveReason Reason;
            public Guid Guid;
            public Vector3F Pos;
            public Vector3F LookAt;
            public Entity Obj;
            public double dist;
            public double doneDist = 0;
            public EMoveCallCmdType Type;
            public bool SetOnlyFalseResult = false;
        }
        RoundZone FarmZone;
        RoundZone SafeZone;
        bool IsRandomMove;
        Unit RandomMoveMob;
        Dictionary<Guid, int> MoveResults = new Dictionary<Guid, int>();
        ConcurrentQueue<MoveRequest> MoveQueue = new ConcurrentQueue<MoveRequest>();
        void MovesTask()
        {
            while (CanWork)
            {
                try
                {
                    if (MoveQueue.Count > 0)
                    {
                        MoveRequest req;
                        if (!MoveQueue.TryDequeue(out req))
                            continue;
                        try
                        {
                            MoveReason = req.Reason;
                            if (req.Type == EMoveCallCmdType.ComeTo)
                            {
                                bool result = false;
                                if (req.Obj == null && Me.Distance(req.Pos) > 500)
                                {
                                    var path = GetServerPath(Me.Location, req.Pos);
                                    if (!path.IsToPointInsideMesh || !path.IsFromPointInsideMesh)
                                        MoveResults[req.Guid] = -1;
                                    else
                                    {
                                        for (int i = 0; i < path.Path.Count - 1; i++)
                                        {
                                            MoveQueue.Enqueue(new MoveRequest()
                                            {
                                                Type = EMoveCallCmdType.ComeTo,
                                                Pos = path.Path[i],
                                                Guid = req.Guid,
                                                dist = 1,
                                                doneDist = 1.5f,
                                                Reason = EMoveReason.ComeToServerPathPoint,
                                                SetOnlyFalseResult = true
                                            });
                                        }
                                        MoveQueue.Enqueue(new MoveRequest()
                                        {
                                            Type = EMoveCallCmdType.ComeTo,
                                            Pos = path.Path[path.Path.Count - 1],
                                            Guid = req.Guid,
                                            dist = 1,
                                            Reason = EMoveReason.ComeToServerPathPoint
                                        });
                                    }
                                }
                                else
                                {
                                    result = MoveTo(new MoveParams()
                                    {
                                        Location = req.Pos,
                                        Obj = req.Obj,
                                        Dist = req.dist,
                                        DoneDist = req.doneDist,
                                        UseNavCall = true,
                                        //IgnoreStuckCheck = true
                                    });
                                    if (req.SetOnlyFalseResult)
                                    {
                                        if (!result)
                                            MoveResults[req.Guid] = -1;
                                    }
                                    else
                                    MoveResults[req.Guid] = result ? 1 : -1;
                                }
                            }
                            else if (req.Type == EMoveCallCmdType.MoveWithLookAt)
                            {
                                try
                                {
                                    IsRandomMove = true;
                                    // bool result = ForceMoveToWithLookTo(req.Pos, req.LookAt);
                                    bool result = MoveTo(new MoveParams()
                                    {
                                        Location = req.Pos,
                                        LookTo = req.LookAt,
                                        Dist = 1f,
                                        UseNavCall = true,
                                        IgnoreStuckCheck = true
                                    });
                                    MoveResults[req.Guid] = result ? 1 : -1;
                                }
                                finally
                                { IsRandomMove = false; }
                            }
                        }
                        finally
                        {
                            MoveReason = EMoveReason.None;
                        }
                    }
                    else
                        Thread.Sleep(5);
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                }
            }
        }
        bool CancelMovesAndWaitStop()
        {
            MoveRequest trash;
            NextRandomMoveAllow = DateTime.UtcNow.AddSeconds(2);
            while (MoveQueue.Count > 0)
                MoveQueue.TryDequeue(out trash);
            var result = CancelMoveTo();
            while (Me.IsMoving)
                Thread.Sleep(50);
            return result;
        }
        void MoveToWithLookAtNoWait(Vector3F pos, Vector3F lookAt, EMoveReason reason)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.MoveWithLookAt;
            req.Pos = pos;
            req.Guid = guid;
            req.LookAt = lookAt;
            req.Reason = reason;
            MoveQueue.Enqueue(req);
        }
     
        bool ComeToAndWaitStop(Vector3F pos, double dist, EMoveReason reason)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.ComeTo;
            req.Pos = pos;
            req.Guid = guid;
            req.dist = dist;
            req.Reason = reason;
            MoveQueue.Enqueue(req);

            while (MoveResults[guid] == 0)
                Thread.Sleep(10);

            var result = MoveResults[guid] == 1;
            MoveResults.Remove(guid);
            while (Me.IsMoving)
                Thread.Sleep(5);
            return result;
        }
        bool ComeToAndWaitStop(Entity obj, double dist, EMoveReason reason)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.ComeTo;
            req.Obj = obj;
            req.Guid = guid;
            req.dist = dist;
            req.Reason = reason;
            MoveQueue.Enqueue(req);

            while (MoveResults[guid] == 0)
                Thread.Sleep(10);

            var result = MoveResults[guid] == 1;
            MoveResults.Remove(guid);
            while (Me.IsMoving)
                Thread.Sleep(5);
            return result;
        }
        void UpdateRandomMoveTimes()
        {
            if (NextRandomMovesDirChange < DateTime.UtcNow)
            {
                NextRandomMovesDirChange = DateTime.UtcNow.AddSeconds(RandomGen.Next(5, 20));
                RandDirLeft = !RandDirLeft;
            }
        }
        public double GetRandomNumber(double minimum, double maximum)
        {
            return RandomGen.NextDouble() * (maximum - minimum) + minimum;
        }
        bool RandDirLeft;
        void TryRandomMoves()
        {
            UpdateRandomMoveTimes();
            if (IsRandomMove && (BestMob == null || BestMob != RandomMoveMob))
            {
                CancelMovesAndWaitStop();
            }
            if (ClassCfg.RandomMovesType == ERandomMovesType.Melee)
            {
                if (NextRandomMoveAllow > DateTime.UtcNow)
                    return;
                if (!IsRandomMove && BestMob != null && !Me.IsMoving && MoveQueue.Count == 0 && Me.Distance(BestMob) < 4)
                {
                    //кружение вокруг цели на melee дистанции
                    var guid = Guid.NewGuid();
                    MoveResults[guid] = 0;

                    var req = new MoveRequest();
                    req.Type = EMoveCallCmdType.MoveWithLookAt;
                    //double angle = 2.0 * Math.PI * RandomGen.NextDouble();
                    double angle = Me.Rotation.Y;
                    if (RandDirLeft)
                        angle += Math.PI / 2f;
                    else
                        angle -= Math.PI / 2f;
                    req.Pos = new Vector3F(BestMob.Location.X + GetRandomNumber(2.5, 5) * Math.Cos(angle), BestMob.Location.Y + GetRandomNumber(2.5, 5) * Math.Sin(angle), Me.Location.Z);


                    req.LookAt = BestMob.Location;
                    req.Guid = guid;
                    RandomMoveMob = BestMob;
                    MoveQueue.Enqueue(req);
                }
            }
            else if (ClassCfg.RandomMovesType == ERandomMovesType.MidRange1)
            {
                if (NextRandomMoveAllow > DateTime.UtcNow)
                    return;
                if (!IsRandomMove && BestMob != null && !Me.IsMoving && MoveQueue.Count == 0 && Me.Distance(BestMob) < 15)
                {
                    //кружение вокруг цели на melee дистанции
                    var guid = Guid.NewGuid();
                    MoveResults[guid] = 0;

                    var req = new MoveRequest();
                    req.Type = EMoveCallCmdType.MoveWithLookAt;
                    //double angle = 2.0 * Math.PI * RandomGen.NextDouble();
                    double angle = Me.Rotation.Y;
                    if (RandDirLeft)
                        angle += Math.PI / 4f;
                    else
                        angle -= Math.PI / 4f;
                    var dist2 = GetRandomNumber(12, 15);
                    if (FarmZone.ObjInZone(BestMob))
                    {
                        req.LookAt = BestMob.Location;
                        req.Pos = new Vector3F(BestMob.Location.X + dist2 * Math.Cos(angle), BestMob.Location.Y + dist2 * Math.Sin(angle), Me.Location.Z);
                    }
                    else
                    {
                        req.LookAt = BestMob.Location; //или в центр зоны?
                        req.Pos = new Vector3F(FarmZone.X + dist2 * Math.Cos(angle), FarmZone.Y + dist2 * Math.Sin(angle), GetNavMeshHeight(new Vector3F(FarmZone.X + dist2 * Math.Cos(angle), FarmZone.Y + dist2 * Math.Sin(angle), 0)));
                    }


                    req.Guid = guid;
                    RandomMoveMob = BestMob;
                    MoveQueue.Enqueue(req);
                }
            }
            else if (ClassCfg.RandomMovesType == ERandomMovesType.MidRange2)
            {
                if (NextBestPosMoveAllow > DateTime.UtcNow)
                    return;
                TryMoveToBestPos();
                NextBestPosMoveAllow = DateTime.UtcNow.AddSeconds(RandomGen.Next(9, 25));
            }
        }
        bool CircleLineIntersectionLogic(Vector3F p1, Vector2F p2, Vector3F p3, float radius)
        {
            double m = ((p2.Y - p1.Y) / (p2.X - p1.X));
            double Constant = (m * p1.X) - p1.Y;

            double b = -(2 * ((m * Constant) + p3.X + (m * p3.Y)));
            double a = (1 + (m * m));
            double c = ((p3.X * p3.X) + (p3.Y * p3.Y) - (radius * radius) + (2 * Constant * p3.Y) + (Constant * Constant));
            double D = ((b * b) - (4 * a * c));
            if (D > 0)
                return true;
            return false;
        }
        uint CountVisibleMobs(Vector3F pos, double angle)
        {
            const double degreeInRads = Math.PI / 180;
            const uint LosDegrees = 15;
            uint result = 0;

            var list = GetAggroMobsInsideFarmZone();

            var direction = new Vector2F((float)Math.Cos(angle) * 30 + pos.X, (float)Math.Sin(angle) * 30 + pos.Y);
            Action calc = new Action(() =>
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (CircleLineIntersectionLogic(pos, direction, list[i].Location, 1))
                    {
                        list.RemoveAt(i);
                        i--;
                        result++;
                    }
                }
            });
            calc();
            for (double a = angle; a < angle + degreeInRads * LosDegrees; a += degreeInRads)
            {
                direction = new Vector2F((float)Math.Cos(angle) * 30 + pos.X, (float)Math.Sin(angle) * 30 + pos.Y);
                calc();
            }
            for (double a = angle; a > angle - degreeInRads * LosDegrees; a -= degreeInRads)
            {
                direction = new Vector2F((float)Math.Cos(angle) * 30 + pos.X, (float)Math.Sin(angle) * 30 + pos.Y);
                calc();
            }

            return result;
        }
        void TryMoveToBestPos()
        {
            //Если на мне висит 1+ моб, или конус от моего зрения не видит всех мобов в фарм зоне 
            //считаем 10 рандомных точек в пределах фарм зоны пытаясь найти лучшую рандомную, откуда будет видно всех врагов
            bool needCheck = GetAggroMobsOnMeCount() > 0;
            uint visibleBest = 0;
            if (!needCheck)
            {
                visibleBest = CountVisibleMobs(Me.Location, Me.Rotation.Y);
                needCheck = visibleBest < GetAggroMobsInsideFarmZoneCount();
            }

            if (!needCheck)
                return;
            if (BestMob.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) > 10)
                return;

            Vector3F best = Vector3F.Zero;
            double distToBest = double.MaxValue;
            for (int i = 0; i < 50; i++)
            {
                double angle = GetRandomNumber(0, Math.PI * 2);
                var pos = new Vector3F(BestMob.Location.X + GetRandomNumber(6, 10) * Math.Cos(angle), BestMob.Location.Y + GetRandomNumber(6, 10) * Math.Sin(angle), Me.Location.Z);
                if (IsInsideNavMesh(pos))
                {
                    var ang = Math.Atan2(BestMob.Location.Y - pos.Y, BestMob.Location.X - pos.X + 0.000001);
                    var count = CountVisibleMobs(pos, ang);
                    if (count > visibleBest)
                    {
                        visibleBest = count;
                        best = pos;
                        distToBest = Me.Distance(pos);
                    }
                    if (count == visibleBest)
                    {
                        if (distToBest > Me.Distance(pos))
                        {
                            best = pos;
                            distToBest = Me.Distance(pos);
                        }
                    }
                }
            }

            if (best != Vector3F.Zero)
            {
                MoveToWithLookAtNoWait(best, BestMob.Location, EMoveReason.MoveToBestFarmPos);
                //ForceMoveToWithLookTo(best, BestMob.Location);
            }
        }
        void InitClassCfg()
        {
            if (Me.Class == EClass.Monk && Me.TalentSpecId == 268)
                ClassCfg = new MonkBrewmasterClassConfig();
            else if (Me.Class == EClass.Druid && Me.TalentSpecId == 102)
                ClassCfg = new DruidBalance();
            else if (Me.Class == EClass.Mage && Me.TalentSpecId == 64)
                ClassCfg = new MageFrost();
            else if (Me.Class == EClass.Shaman && Me.TalentSpecId == 262)
                ClassCfg = new ShamanElemental();
            else if (Me.Class == EClass.Hunter && Me.TalentSpecId == 254)
                ClassCfg = new HunterMarkshman();
            else
            {
                Log("Unknown ClassSpec: " + Me.Class + "[" + Me.TalentSpecId + "]", Me.Name);
                ClassCfg = new ClassConfig();
            }
        }

        object VarLocker = new object();
        void SetupVariables()
        {
            while (CanWork)
            {
                try
                {
                    Thread.Sleep(100);
                    lock (VarLocker)
                    {
                        Totem = null;
                        aggroMobsAll.Clear();
                        aggroMobsOnParty.Clear();
                        aggroMobsOnMe.Clear();
                        aggroMobsInsideFarmZone.Clear();
                        mobsCanBePulled.Clear();
                        GroupPlayers.Clear();
                        LootableMobs.Clear();
                        var mobs = GetEntities<Unit>();
                        var players = GetEntities<Player>();
                        foreach (var p in players)
                        {
                            if (Me.IsInSameGroupWith(p))
                                GroupPlayers.Add(p);
                        }
                        foreach (var mob in mobs)
                        {
                            if (mob.Id == BlackOxTotem &&
                                (mob.CreatorGuid == Me.Guid || GroupPlayers.Exists(p => p.Guid == mob.CreatorGuid)))
                            {
                                Totem = mob;
                                break;
                            }
                        }
                        foreach (var t in GetThreats())
                        {
                            aggroMobsAll.Add(t.Obj);
                            if (t.Obj.Target == Me)
                                aggroMobsOnMe.Add(t.Obj);
                        }
                        foreach (var mob in mobs)
                        {
                            //if (FarmCfg.MobIDs.ContainsKey(mob.Id))
                            if (CanAttack(mob, 0) && mob.IsAlive)
                            {
                                if (Totem != null && mob.Target == Totem)
                                    aggroMobsAll.Add(mob);
                                foreach (var p in GroupPlayers)
                                {
                                    if (mob.Target == p)
                                    {
                                        aggroMobsAll.Add(mob);
                                        aggroMobsOnParty.Add(mob);
                                    }
                                }
                            }
                            if (!mob.IsAlive && mob.Distance(Totem) < 15 && mob.IsLootable)
                                LootableMobs.Add(mob);
                        }
                        foreach (var mob in aggroMobsAll)
                        {
                            if (FarmZone.ObjInZone(mob))
                                aggroMobsInsideFarmZone.Add(mob);
                        }
                        foreach (var mob in mobs)
                        {
                            if (FarmCfg.MobIDs.ContainsKey(mob.Id) && mob.IsAlive
                                && !aggroMobsAll.Exists(b => b == mob) && mob.TargetGuid == WowGuid.Zero
                                &&
                                (
                                        (FarmCfg.PullRoundZone != null && FarmCfg.PullRoundZone.ObjInZone(mob))
                                    || (FarmCfg.PullPolygoneZone != null && FarmCfg.PullPolygoneZone.ObjInZone(mob))
                                    || (ClassCfg.PullSpellId != 0 && mob.Distance(Me) < PullSpellRange)
                                )
                                )
                            {
                                if (GetVar(mob, "los") == null)
                                    mobsCanBePulled.Add(mob);
                                else
                                {
                                    var dt = (DateTime)GetVar(mob, "los");
                                    if (dt.AddSeconds(60) < DateTime.UtcNow)
                                        mobsCanBePulled.Add(mob);
                                }
                            }
                        }
                        BestMob = GetBestMob();
                    }

                    var s = BestMob != null ? (BestMob.Name + "[" + Me.Distance(BestMob) + "]") : "null";
                    //Console.WriteLine(State + "|" + MoveReason + "[" + MoveQueue.Count + "]" + "|" + s);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
        bool HealParty()
        {
            var players = GetEntities<Player>();

            foreach (var p in players)
            {
                if (Me.IsInSameGroupWith(p))
                {
                    foreach (var spellData in ClassCfg.PartyHealSpellIds)
                    {
                        var spellInstant = IsSpellInstant(spellData.Id);
                        if (CheckAllConditions(spellData, p))
                        {
                            var spellCastRange = Math.Max(0, GetSpellCastRange(spellData.Id) - 1);
                            if (spellCastRange != 0 && spellCastRange < Me.Distance(p))
                                ComeToAndWaitStop(p, Math.Max(0.5f, spellCastRange - 2), EMoveReason.PartyHeal);
                            var cr = UseSingleSpell(spellData.Id, !spellInstant, p);
                            if (cr == ESpellCastError.SUCCESS)
                                return true;
                        }
                    }
                }
            }
            return false;
        }
        bool BuffSelf()
        {
            foreach (var spellData in ClassCfg.BuffSpellIds)
            {
                var spellInstant = IsSpellInstant(spellData.Id);
                if (CheckAllConditions(spellData, Me))
                {
                    if (UseSingleSpell(spellData.Id, !spellInstant, Me) == ESpellCastError.SUCCESS)
                        return true;
                }
            }
            return false;
        }
        bool HealSelf()
        {
            foreach (var spellData in ClassCfg.SelfHealSpellIds)
            {
                var spellInstant = IsSpellInstant(spellData.Id);
                if (CheckAllConditions(spellData, Me))
                {
                    if (UseSingleSpell(spellData.Id, !spellInstant, Me) == ESpellCastError.SUCCESS)
                        return true;
                }
            }
            return false;
        }
        bool UseTaunt()
        {
            if (ClassCfg.TauntSpellId != 0 && GetAggroMobsOnPartyCount() > 0)
            {
                var mob = GetBestMobForTaunt();
                if (mob == null)
                    return false;
                var spellInstant = IsSpellInstant(ClassCfg.TauntSpellId);
                var spellCastRange = Math.Max(0, GetSpellCastRange(ClassCfg.TauntSpellId) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(mob))
                    return false;//не бежим в ебеня сагрить его?
                    //ComeToAndWaitStop(mob, Math.Max(0.5f, spellCastRange - 2));
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                {
                    CancelMovesAndWaitStop();
                }
                if (UseSingleSpell(ClassCfg.TauntSpellId, !spellInstant, mob) == ESpellCastError.SUCCESS)
                    return true;
            }
            return false;
        }
       
        bool CheckNeedResAnothers()
        {
            if (ClassCfg.ResSpellIds.Count > 0)
            {
                if (NextSpellResTry < DateTime.UtcNow)
                {
                    NextSpellResTry = DateTime.UtcNow.AddSeconds(RandomGen.Next(10, 30));
                    var corpses = GetEntities<Corpse>();
                    foreach (var p in Group.GetMembers())
                    {
                        Entity deadMember = null;
                        var player = GetEntity(p.Guid) as Player;
                        if (player != null && !player.IsAlive)
                            deadMember = player;
                        if (deadMember == null)
                        {
                            foreach (var c in corpses)
                            {
                                if (c.OwnerGuid == p.Guid && (c.CorpseType == ECorpseType.ResurrectablePVE || c.CorpseType == ECorpseType.ResurrectablePVP))
                                {
                                    deadMember = c;
                                    break;
                                }
                            }
                        }

                        if (deadMember != null)
                        {
                            foreach (var spell in ClassCfg.ResSpellIds)
                            {
                                var spellInstant = IsSpellInstant(spell.Id);
                                var spellCastRange = Math.Max(0, GetSpellCastRange(spell.Id) - 1);
                                if (spellCastRange != 0 && spellCastRange < Me.Distance(deadMember))
                                    ComeToAndWaitStop(deadMember, Math.Max(0.5f, spellCastRange - 2), EMoveReason.ComeToResPlayer);
                                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                                    CancelMovesAndWaitStop();
                                var cr = UseSingleSpell(spell.Id, !spellInstant, deadMember);
                                if (cr == ESpellCastError.LINE_OF_SIGHT)
                                {
                                    ComeToAndWaitStop(deadMember, 3, EMoveReason.ComeToResPlayer);
                                    if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                                        CancelMovesAndWaitStop();
                                    cr = UseSingleSpell(spell.Id, !spellInstant, deadMember);
                                }
                                if (cr == ESpellCastError.SUCCESS)
                                    return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
        bool CheckShapeshift()
        {
            if (ClassCfg.ShapeshiftSpellId != 0 && Me.ShapeshiftForm != ClassCfg.RequiredShapeshift)
            {
                var spellInstant = IsSpellInstant(ClassCfg.ShapeshiftSpellId);
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                {
                    CancelMovesAndWaitStop();
                }
                var result = SpellManager.CastSpell(ClassCfg.ShapeshiftSpellId, Me);
                return result == ESpellCastError.SUCCESS;
            }
            return false;
        }
        bool UseTotem()
        {
            if (ClassCfg.TotemSpellId != 0 && Totem == null)
            {
                var spellInstant = IsSpellInstant(ClassCfg.TotemSpellId);
                var spellCastRange = Math.Max(0, GetSpellCastRange(ClassCfg.TotemSpellId) - 1);
                var randPoint2D = FarmCfg.TotemInstallZone.GetRandomPoint();
                var castPoint = new Vector3F(randPoint2D.X, randPoint2D.Y, GetNavMeshHeight(new Vector3F(randPoint2D.X, randPoint2D.Y,0)));
                if (spellCastRange != 0 && spellCastRange < Me.Distance(castPoint))
                    ComeToAndWaitStop(castPoint, Math.Max(0.5f, spellCastRange - 2), EMoveReason.UseTotem);
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                    CancelMovesAndWaitStop();
                var result = SpellManager.CastSpell(ClassCfg.TotemSpellId, null, castPoint);
                return result == ESpellCastError.SUCCESS;
            }
            return false;
        }

        bool IsSpellInstant(uint id)
        {
            var spell = SpellManager.GetSpell(id);
            if (spell == null)
                return true;
            return spell.CastTime == 0;
        }

        double GetSpellCastRange(uint id)
        {
            var spell = SpellManager.GetSpell(id);
            if (spell == null)
                return 0;
            return spell.GetMaxCastRange();
        }


        List<Unit> LootableMobs = new List<Unit>();
        List<Player> GroupPlayers = new List<Player>();

        void TurnIfNeed(Entity target, bool force = false)
        {
            if ((Me.GetAngle(target) > 20 && Me.GetAngle(target) < 340) || force)
            {
                TurnDirectly(target);
                Thread.Sleep(111);
            }
        }

        bool WaitTillAction(uint waitTime, Func<bool> fn)
        {
            var dt = DateTime.UtcNow.AddMilliseconds(waitTime);
            while (dt > DateTime.UtcNow)
            {
                Thread.Sleep(50);
                if (fn())
                    return true;
            }
            return false;
        }
        bool CanContinuePull()
        {
            if ((State & EFarmState.Farming) == 0)
                return false;
            if ((State & EFarmState.DontPull) != 0)
                return false;
            if (GetMobsCanBePulledCount() == 0)
                return false;
            if (Me.HpPercents < 70)
                return false;
            //увеличить при необходимости
            if ((GetAggroMobsAllCount() - GetAggroMobsOnMeCount()) > FarmCfg.DontPullWhenXMobsInFarmZone)
                return false;
            var d = Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0));
            uint r = 0;

            uint threatsOnMe = 0;
            foreach (var obj in GetThreats())
                if (obj.Obj.Target == Me)
                    threatsOnMe++;


            foreach (var obj in GetThreats())
                if (obj.Obj.Distance(Me) < 10 && obj.Obj.Target == Me)
                    r++;
            if (FarmCfg.PullСomplexity == EPullСomplexity.Max)
            {
                if (r > 1 || threatsOnMe > 5)
                    return false;
                if ((r > 0 || threatsOnMe > 3) && d > 25)
                    return false;
            }
            if (FarmCfg.PullСomplexity == EPullСomplexity.Mid)
            {
                if (r > 1 || threatsOnMe > 3)
                    return false;
                if ((r > 0 || threatsOnMe > 1) && d > 25)
                    return false;
            }
            if (FarmCfg.PullСomplexity == EPullСomplexity.Safe)
            {
                if (r > 0 || threatsOnMe > 2)
                    return false;
            }
            return true;
        }
        bool CheckDistToFarmZone()
        {
            if (Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) > 120)
            {
                ComeToAndWaitStop(FarmCfg.StartFarmPoint, 2, EMoveReason.ComeToFarmStartPoint);
                return true;
            }

            bool forceBack = false;
            if (IsPuller && !CanContinuePull() && Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) > FarmZone.Radius)
                forceBack = true;
            if (!forceBack && (NextCheckDist > DateTime.UtcNow || IsPuller))
                return false;
            NextCheckDist = DateTime.UtcNow.AddSeconds(RandomGen.Next(13, 31));
            var dist = FarmZone.Radius;
            if (ClassCfg.RandomMovesType == ERandomMovesType.MidRange1 || ClassCfg.RandomMovesType == ERandomMovesType.MidRange2)
                dist = 20;
            if (forceBack)
                dist = 5;

            if (Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) > dist)
            {
                var posToCome = new Vector3F(FarmZone.X, FarmZone.Y, GetNavMeshHeight(new Vector3F(FarmZone.X, FarmZone.Y, 0)));
                ComeToAndWaitStop(posToCome, Math.Max(0.5f, dist - RandomGen.Next(2, 5)), EMoveReason.BackToFarmZone);
                return true;
            }
            return false;
        }
        Unit GetBestMobForPull()
        {
            Unit result = null;
            double dist = double.MaxValue;
            foreach (var mob in GetMobsCanBePulled())
            {
                if (dist > Me.Distance(mob))
                {
                    result = mob;
                    dist = Me.Distance(mob);
                }
            }
            return result;
        }
        Unit GetBestMobForTaunt()
        {
            Unit result = null;
            double dist = double.MaxValue;
            foreach (var mob in GetAggroMobsOnParty())
            {
                if (dist > Me.Distance(mob))
                {
                    result = mob;
                    dist = Me.Distance(mob);
                }
            }
            return result;
        }

        Unit GetBestMob()
        {
            Unit result = null;
            BestMobInsideFZ = false;
            double dist = double.MaxValue;
            uint prio = 0; //чем выше - тем выше приоритет
            foreach (var mob in GetAggroMobsInsideFarmZone())
            {
                uint mobPrio = 0;
                if (FarmCfg.MobIDs.ContainsKey(mob.Id))
                    mobPrio = FarmCfg.MobIDs[mob.Id];
                if (mobPrio > prio)
                {
                    prio = mobPrio;
                    result = mob;
                    BestMobInsideFZ = true;
                    dist = Me.Distance(mob);
                }
                else if (mobPrio == prio)
                {
                    if (dist > Me.Distance(mob))
                    {
                        result = mob;
                        BestMobInsideFZ = true;
                        dist = Me.Distance(mob);
                    }
                }
            }
            if (result == null)
            {
                foreach (var mob in GetAggroMobsAll())
                {
                    uint mobPrio = 0;
                    if (FarmCfg.MobIDs.ContainsKey(mob.Id))
                        mobPrio = FarmCfg.MobIDs[mob.Id];
                    if (mobPrio > prio)
                    {
                        prio = mobPrio;
                        result = mob;
                        BestMobInsideFZ = true;
                        dist = Me.Distance(mob);
                    }
                    else if (mobPrio == prio)
                    {
                        if (dist > Me.Distance(mob))
                        {
                            result = mob;
                            dist = Me.Distance(mob);
                            BestMobInsideFZ = false;
                        }
                    }
                }
            }
            return result;
        }
        bool PullMobs()
        {
            if (ClassCfg.PullSpellId != 0 && GetMobsCanBePulledCount() > 0)
            {
                if (!CanContinuePull())
                    return false;
                var mob = GetBestMobForPull();
                if (mob == null)
                    return false;
                var spellInstant = IsSpellInstant(ClassCfg.PullSpellId);
                var spellCastRange = Math.Max(0, GetSpellCastRange(ClassCfg.PullSpellId) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(mob))
                    ComeToAndWaitStop(mob, Math.Max(0.5f, spellCastRange - 2), EMoveReason.ComeToMobForPull);
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                    CancelMovesAndWaitStop();
                if (UseSingleSpell(ClassCfg.PullSpellId, !spellInstant, mob) == ESpellCastError.SUCCESS)
                    return true;
            }
            return false;
            
        }
        bool UseAttackSpells()
        {
            if (BestMob == null)
                return false;
            if (ClassCfg.PullSpellId != 0 && CanContinuePull())
                return false;

            foreach (var spell in ClassCfg.AttackSpellIds)
            {
                var spellInstant = IsSpellInstant(spell.Id);
                var spellCastRange = Math.Max(0, GetSpellCastRange(spell.Id) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(BestMob))
                {
                    if ((State & EFarmState.Farming) != 0)
                    {
                        if (!FarmCfg.ProtectPullers && !BestMobInsideFZ)
                            continue;
                    }
                    ComeToAndWaitStop(BestMob, Math.Max(0.5f, spellCastRange - 2), EMoveReason.ComeToMobForAttack);
                }

                bool spellCanMoveWhileCasting = false;
                //пока так, потом надо функцию в АПИ
                if (spell.Id == 120360 || spell.Id == 257044 || spell.Id == 56641)
                    spellCanMoveWhileCasting = true;
                if (spellCastRange == 0 || (spellCastRange != 0 && spellCastRange >= Me.Distance(BestMob)))
                {
                    if (UseSingleSpell(spell.Id, !spellInstant && !spellCanMoveWhileCasting, BestMob, spell.SendLocation ? BestMob.Location : new Vector3F()) == ESpellCastError.SUCCESS)
                        return true;
                }
            }
            return false;
        }

        void CancelMounts()
        {
            foreach (var a in Me.GetAuras())
            {
                if (a.IsPartOfSkillLine(777))
                    a.Cancel();
            }
        }
        void WaitCasts()
        {
            while (SpellManager.IsCasting || SpellManager.IsChanneling)
                Thread.Sleep(100);
        }
        ESpellCastError UseSingleSpell(uint id, bool waitCasts, Entity target = null, Vector3F pos = new Vector3F(), bool autoTurn = true)
        {
            CancelMounts();
            if (waitCasts && (Me.IsMoving || MoveQueue.Count > 0))
                CancelMovesAndWaitStop();
            if (target != null && target != Me && Me.Target != target)
                SetTarget(target);
            //todo, спелы которые не требуют угла, но бьют с углом
            if (autoTurn && (id == 120360 || id == 257044 || id == 56641))
                TurnIfNeed(target, false);
            var crPre = SpellManager.CheckCanCast(id, target);
            if (autoTurn && crPre == ESpellCastError.UNIT_NOT_INFRONT)
                TurnIfNeed(target, true);
            var cr = SpellManager.CastSpell(id, target, pos);
            if (autoTurn && cr == ESpellCastError.UNIT_NOT_INFRONT)
                TurnIfNeed(target, true);
            if (cr == ESpellCastError.LINE_OF_SIGHT)
                SetVar(target, "los", DateTime.UtcNow);
            if (cr == ESpellCastError.SUCCESS)
            {
                if (waitCasts)
                    WaitCasts();
                return cr;
            }
            return cr;
        }
        List<uint> MobsCastersNeedPreventCast = new List<uint>() { 122240 };
        Unit GetBestMobForSpellcastPreventing()
        {
            //статически пропишу айди кастеров которых повстречаю
            Unit result = null;
            double dist = 0;
            foreach (var mob in GetAggroMobsAll())
            {
                if (!MobsCastersNeedPreventCast.Contains(mob.Id))
                    continue;
                var d = Me.Distance(mob);
                if (mob.IsMoving || d < 10 || mob.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) > 35)
                    continue;
                if (d > dist)
                {
                    result = mob;
                    dist = d;
                }
            }
            return result;
        }
        bool PreventSpellcast()
        {
            if (ClassCfg.SpellcastPreventSpellId != 0)
            {
                var mob = GetBestMobForSpellcastPreventing();
                if (mob == null)
                    return false;
                if (!SpellManager.IsSpellReady(ClassCfg.SpellcastPreventSpellId))
                    return false;
                var spellInstant = IsSpellInstant(ClassCfg.SpellcastPreventSpellId);
                var spellCastRange = Math.Max(0, GetSpellCastRange(ClassCfg.SpellcastPreventSpellId) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(mob))
                    ComeToAndWaitStop(mob, Math.Max(0.5f, spellCastRange - 2), EMoveReason.ComeToMobForAttack);
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                    CancelMovesAndWaitStop();
                if (UseSingleSpell(ClassCfg.SpellcastPreventSpellId, !spellInstant, BestMob, new Vector3F(), false) == ESpellCastError.SUCCESS)
                    return true;
            }
            return false;
        }
        bool CollectLoot()
        {
            PickupLoot();
            if (LootableMobs.Count < 20)
                return false;
            if (NextPickup > DateTime.UtcNow)
                return false;
            NextPickup = DateTime.UtcNow.AddSeconds(RandomGen.Next(2, 10));
            Unit first = LootableMobs.OrderBy(m => m.Distance(Me)).FirstOrDefault(m => GetVar(m, "looted") == null);
            if (first != null)
            {
                if (Me.IsMoving || MoveQueue.Count > 0)
                {
                    CancelMovesAndWaitStop();
                }
                var maxRange = MaxInteractionDistance - 1f;
                if (Me.Distance(first) > maxRange)
                {
                    ComeToAndWaitStop(first, maxRange, EMoveReason.CollectLoot);
                    Thread.Sleep(111);
                }
                if (!OpenLoot(first))
                    Log("Failed to open loot: " + GetLastError(), Me.Name);
                else
                {
                    if (WaitTillAction(3000, new Func<bool>(() => { return CanPickupLoot(); })))
                    {
                        if (PickupLoot())
                        {
                            SetVar(first, "looted", true);
                            Thread.Sleep(111);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        public bool Disenchant()
        {
            if (!FarmCfg.DisenchantItems)
                return false;
            if (NextDisenchant > DateTime.UtcNow)
                return false;
            NextDisenchant = DateTime.UtcNow.AddSeconds(RandomGen.Next(2, 10));
            var toEquip = EquipBestArmorAndWeapon();
            foreach (var item in ItemManager.GetItems())
            {
                if (item.Place >= EItemPlace.InventoryItem && item.Place <= EItemPlace.Bag4)
                {
                    if (item.ItemQuality == EItemQuality.Uncommon)
                    {
                        if (toEquip != null && toEquip.Contains(item))
                            continue;
                        if (Me.IsMoving || MoveQueue.Count > 0)
                        {
                            CancelMovesAndWaitStop();
                        }
                        if (item.Disenchant() == ESpellCastError.SUCCESS)
                            return true;
                    }
                }
            }
            return false;
        }


        bool CheckAllConditions(SpellCastData data, Unit target)
        {
            foreach (var cond in data.Conditions)
            {
                if (cond.Type == EValueType.CreaturesCount)
                {
                    var condCount = GetEntities().Count(e => e.Id == cond.Value);
                    if (!ConditionCompare(cond.Comparsion, condCount, cond.Value2))
                        return false;
                }
                if (cond.Type == EValueType.HpPercent)
                {
                    if (!ConditionCompare(cond.Comparsion, target.HpPercents, cond.Value))
                        return false;
                }
                if (cond.Type == EValueType.MinionExists && target as Player != null)
                {
                    if (cond.Value == 0 && cond.Comparsion == EComparsion.Equal)
                    {
                        if ((target as Player).MinionGuid != WowGuid.Zero)
                            return false;
                    }
                }
                
                if (cond.Type == EValueType.MonkStagger && target as Player != null)
                {
                    if (!ConditionCompare(cond.Comparsion, (int)(target as Player).MonkStagger, cond.Value))
                        return false;
                }
                if (cond.Type == EValueType.TargetInFarmZone)
                {
                    if (cond.Comparsion == EComparsion.Equal)
                        return FarmZone.ObjInZone(target);
                    else if (cond.Comparsion == EComparsion.NotEqual)
                        return !FarmZone.ObjInZone(target);
                }
            }
            return true;
        }
        bool ConditionCompare(EComparsion comparisonType, int value1, int value2)
        {
            switch (comparisonType)
            {
                case EComparsion.Equal:
                    return value1 == value2;
                case EComparsion.NotEqual:
                    return value1 != value2;
                case EComparsion.Greater:
                    return value1 > value2;
                case EComparsion.GreaterOrEqual:
                    return value1 >= value2;
                case EComparsion.Less:
                    return value1 < value2;
                case EComparsion.LessOrEqual:
                    return value1 <= value2;
                default:
                    break;
            }
            return false;
        }

        List<Item> EquipBestArmorAndWeapon()
        {
            if (!FarmCfg.AutoEquipItems)
                return null;
            var result = new List<Item>();
            var equipCells = new Dictionary<EEquipmentSlot, Item>();
            foreach (EEquipmentSlot value in Enum.GetValues(typeof(EEquipmentSlot)))
                equipCells.Add(value, null);

            foreach (var item in ItemManager.GetItems())
            {
                if (item.Place == EItemPlace.Bag1 || item.Place == EItemPlace.Bag2 ||
                    item.Place == EItemPlace.Bag3 || item.Place == EItemPlace.Bag4 ||
                    item.Place == EItemPlace.InventoryItem || item.Place == EItemPlace.Equipment)
                {
                    if (item.ItemClass == EItemClass.Armor || item.ItemClass == EItemClass.Weapon)
                    {
                        if (item.Place != EItemPlace.Equipment && !item.CanEquipItem())
                            continue;
                        if (item.RequiredLevel > Me.Level)
                            continue;
                        if (item.ItemClass == EItemClass.Weapon && !ClassCfg.WeaponType.Contains((EItemSubclassWeapon)item.ItemSubClass))
                            continue;
                        if (item.ItemClass == EItemClass.Armor && !ClassCfg.ArmorType.Contains((EItemSubclassArmor)item.ItemSubClass))
                            continue;

                        var itemEquipType = GetItemEPlayerPartsType(item.InventoryType);
                        //одноручные пухи тут проверка
                        if (itemEquipType == EEquipmentSlot.OffHand)
                            continue;

                        if (equipCells[itemEquipType] == null)
                            equipCells[itemEquipType] = item;
                        else
                        {
                            double bestCoef = 0;
                            double curCoef = 0;
                            bestCoef = equipCells[itemEquipType].Level;
                            curCoef = item.Level;
                            if (bestCoef < curCoef)
                                equipCells[itemEquipType] = item;
                        }
                    }
                }
            }
            foreach (var b in equipCells.Keys.ToList())
            {
                if (equipCells[b] != null && equipCells[b].Place != EItemPlace.Equipment)
                    result.Add(equipCells[b]);
            }

            return result;
        }
        EEquipmentSlot GetItemEPlayerPartsType(EInventoryType type)
        {
            switch (type)
            {
                /*  case EInventoryType.NonEquip:
                      break;*/
                case EInventoryType.Head:
                    return EEquipmentSlot.Head;
                case EInventoryType.Neck:
                    return EEquipmentSlot.Neck;
                case EInventoryType.Shoulders:
                    return EEquipmentSlot.Shoulders;
                case EInventoryType.Body:
                    return EEquipmentSlot.Body;
                case EInventoryType.Chest:
                    return EEquipmentSlot.Chest;
                case EInventoryType.Waist:
                    return EEquipmentSlot.Waist;
                case EInventoryType.Legs:
                    return EEquipmentSlot.Legs;
                case EInventoryType.Feet:
                    return EEquipmentSlot.Feet;
                case EInventoryType.Wrists:
                    return EEquipmentSlot.Wrists;
                case EInventoryType.Hands:
                    return EEquipmentSlot.Hands;
                case EInventoryType.Finger:
                    return EEquipmentSlot.Finger1;
                case EInventoryType.Trinket:
                    return EEquipmentSlot.Trinket1;
                case EInventoryType.Weapon:
                    return EEquipmentSlot.MainHand;
                case EInventoryType.Shield:
                    return EEquipmentSlot.OffHand;

                case EInventoryType.Ranged:
                    return EEquipmentSlot.MainHand;
                case EInventoryType.Cloak:
                    return EEquipmentSlot.Cloak;
                case EInventoryType.TwoHandedWeapon:
                    return EEquipmentSlot.MainHand;
                case EInventoryType.Bag:
                    break;
                case EInventoryType.Tabard:
                    return EEquipmentSlot.Tabard;
                case EInventoryType.Robe:
                    return EEquipmentSlot.Chest;
                case EInventoryType.MainHandWeapon:
                    return EEquipmentSlot.MainHand;
                case EInventoryType.OffHandWeapon:
                    return EEquipmentSlot.OffHand;
                case EInventoryType.Holdable:
                    return EEquipmentSlot.OffHand;
                case EInventoryType.RangedRight:
                    return EEquipmentSlot.MainHand;
            }
            return EEquipmentSlot.Ranged;
        }

        public void PluginStop()
        {
            CanWork = false;
            MoveQueue = new ConcurrentQueue<MoveRequest>();

            CancelMoveTo();
            MoveForward(false);
            MoveBackward(false);
            StrafeRight(false);
            StrafeLeft(false);
            Ascend(false);
            Descend(false);
            SetMoveStateForClient(false);
        }
    }
}
