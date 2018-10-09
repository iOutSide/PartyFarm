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
    [Flags]
    enum EFarmState
    {
        Farming = 1,
        LocalRepair = 2,
        LocalSell = 4,
        GlobalAuction = 8,
        DefOrRegroup = 16,
        NeedCallRepairman = 32
    }
    public class PartyFarm : Core
    {
        static uint BlackOxTotem = 61146;
        static double MaxInteractionDistance = 5f;

        string NeedRepairMessage = "NeedRepair";
        string RepairDoneMessage = "RepairDone";
        string NeedSellMessage = "NeedSell";
        string SellDoneMessage = "SellDone";

        EFarmState State = EFarmState.Farming;

        bool CanWork;
        FarmConfigDefault FarmCfg;
        ClassConfig ClassCfg;
        Unit Totem;
        Unit BestMob;
        List<Unit> MobsCanBePulled = new List<Unit>();
        List<Unit> AggroMobsAll = new List<Unit>();
        List<Unit> AggroMobsOnParty = new List<Unit>();
        List<Unit> AggroMobsOnMe = new List<Unit>();
        List<Unit> AggroMobsInsideFarmZone = new List<Unit>();

        Random RandomGen = new Random((int)DateTime.UtcNow.Ticks);
        DateTime NextDeleteTrashItem = DateTime.UtcNow;
        DateTime NextDisenchant = DateTime.UtcNow;
        DateTime NextCheckDist = DateTime.UtcNow; 
        DateTime NextPickup = DateTime.UtcNow;


        DateTime NextRepairOrSellMessageAllow = DateTime.UtcNow;
        DateTime NextRepairOrSellAllow = DateTime.UtcNow;
        void UpdateFarmState()
        {
            bool haveActiveRepairOrSellRequests = false;
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
                if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0 && NextRepairOrSellMessageAllow < DateTime.UtcNow)
                {
                    NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                    SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, NeedRepairMessage);
                }
                foreach (var p in GetEntities<Player>())
                {
                    if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0 && NextRepairOrSellMessageAllow < DateTime.UtcNow)
                    {
                        NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                        SendMessageToWoWCharacter(p.ServerName, p.Name, NeedRepairMessage);
                    }
                }
            }
            else
                State &= ~EFarmState.LocalRepair;




            bool needSell = ItemManager.GetFreeInventorySlotsCount() < 10;
            if (needSell && !string.IsNullOrEmpty(FarmCfg.Repairman))
            {
                State |= EFarmState.LocalSell;
                if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0 && NextRepairOrSellMessageAllow < DateTime.UtcNow)
                {
                    NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                    SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, NeedSellMessage);
                }
                foreach (var p in GetEntities<Player>())
                {
                    if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0 && NextRepairOrSellMessageAllow < DateTime.UtcNow)
                    {
                        NextRepairOrSellMessageAllow = DateTime.UtcNow.AddSeconds(20);
                        SendMessageToWoWCharacter(p.ServerName, p.Name, NeedSellMessage);
                    }
                }
            }
            else
                State &= ~EFarmState.LocalSell;
        }
        Dictionary<string, DateTime> RepairRequests = new Dictionary<string, DateTime>();
        Dictionary<string, DateTime> SellRequests = new Dictionary<string, DateTime>();
        void PartyFarm_onCharacterMessage(string SenderServerName, string SenderName, string Message)
        {
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
                            Thread.Sleep(1500);
                            q = 0;
                        }
                        i.Sell(false);
                        Thread.Sleep(RandomGen.Next(55, 112));
                        q++;
                    }
                }
            }
        }
        public void PluginRun()
        {
            try
            {
                onCharacterMessage += PartyFarm_onCharacterMessage;
                CanWork = true;
                //EnableRandomJumpsOnMoves();

                var cfgPath = "Configs/" + Me.Name + "_" + CurrentServer.Name + ".txt";
                FarmCfg = (FarmConfigDefault)ConfigLoader.LoadConfig(cfgPath, typeof(FarmConfigDefault), new FarmConfigDefault());
                InitClassCfg();
                FarmZone = new RoundZone(FarmCfg.TotemInstallZone.X, FarmCfg.TotemInstallZone.Y, FarmCfg.FarmZoneRadius);



                ClearLogs();
                Task.Run(() => MovesTask());

                while (GameState == EGameState.Ingame)
                {
                    UpdateFarmState();
                    if ((State & EFarmState.LocalRepair) != 0 && NextRepairOrSellAllow < DateTime.UtcNow)
                    {
                        Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor && npc.IsArmorer);
                        if (npcRepairman != null)
                        {
                            NextRepairOrSellAllow = DateTime.UtcNow.AddSeconds(30);
                            ComeToAndWaitStop(npcRepairman, 1f);

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
                            }
                        }
                    }
                    if ((State & EFarmState.LocalSell) != 0 && NextRepairOrSellAllow < DateTime.UtcNow)
                    {
                        Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor && npc.IsArmorer);
                        if (npcRepairman != null)
                        {
                            NextRepairOrSellAllow = DateTime.UtcNow.AddSeconds(30);
                            ComeToAndWaitStop(npcRepairman, 1f);

                            SellAllTrash();
                            if (string.Compare(Me.Name, FarmCfg.Repairman, true) == 0)
                                SendMessageToWoWCharacter(CurrentServer.Name, Me.Name, SellDoneMessage);
                            foreach (var p in GetEntities<Player>())
                            {
                                if (string.Compare(p.Name, FarmCfg.Repairman, true) == 0)
                                    SendMessageToWoWCharacter(p.ServerName, p.Name, SellDoneMessage);
                            }
                            State &= ~EFarmState.LocalSell;
                        }
                    }

                    if ((State & EFarmState.NeedCallRepairman) != 0 && FarmCfg.RepairmanMountSpellId != 0)
                    {
                        if (FarmCfg.RepairSummonPoint != Vector3F.Zero && Me.Distance(FarmCfg.RepairSummonPoint) > 3)
                            ComeToAndWaitStop(FarmCfg.RepairSummonPoint, 1);
                        Unit npcRepairman = GetEntities<Unit>().FirstOrDefault(npc => npc.IsVendor && npc.IsArmorer);
                        if (!Me.IsInCombat && npcRepairman == null)
                            SpellManager.CastSpell(FarmCfg.RepairmanMountSpellId);
                        Thread.Sleep(1000);
                    }
                    else if ((State & EFarmState.Farming) != 0)
                    {
                        if (!Me.IsAlive)
                        {
                            Thread.Sleep(1000);
                            continue;
                        }
                        CancelMounts();
                        WaitCasts();
                        SetupVariables();
                        DeteleTrashItems();
                        if (CheckShapeshift())
                            continue;
                        if (UseTotem())
                            continue;
                        if (HealSelf())
                            continue;
                        if (HealParty())
                            continue;
                        if (PullMobs())
                            continue;
                        if (BuffSelf())
                            continue;
                        if (CollectLoot())
                            continue;
                        if (Disenchant())
                            continue;
                        if (CheckDistToFarmZone())
                            continue;
                        if (UseTaunt())
                            continue;
                        TryRandomMoves();

                        if (UseAttackSpells())
                            continue;


                        Thread.Sleep(100);
                    }
                }
            }
            finally
            {
                //DisableRandomJumpsOnMoves();
                CanWork = false;
            }
        }

        

        void DeteleTrashItems()
        {
            if (!FarmCfg.DeleteTrashItems)
                return;
            if (NextDeleteTrashItem > DateTime.UtcNow)
                return;
            foreach (var i in ItemManager.GetItems())
            {
                if (i.ItemQuality == EItemQuality.Poor && i.Place >= EItemPlace.InventoryBag && i.Place <= EItemPlace.Bag4
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
            public Guid Guid;
            public Vector3F Pos;
            public Vector3F LookAt;
            public Entity Obj;
            public double dist;
            public EMoveCallCmdType Type;
        }
        DateTime NextRandomMoveAllow = DateTime.UtcNow;
        DateTime NextBestPosMoveAllow = DateTime.UtcNow;
        RoundZone FarmZone;
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
                        if (req.Type == EMoveCallCmdType.ComeTo)
                        {
                            bool result = MoveTo(new MoveParams() {
                                Location = req.Pos,
                                Obj = req.Obj,
                                Dist = req.dist,
                                IgnoreStuckCheck = true
                            });
                            MoveResults[req.Guid] = result ? 1 : -1;
                        }
                        else if (req.Type == EMoveCallCmdType.MoveWithLookAt)
                        {
                            try
                            {
                                IsRandomMove = true;
                                bool result = ForceMoveToWithLookTo(req.Pos, req.LookAt);
                                MoveResults[req.Guid] = result ? 1 : -1;
                            }
                            finally
                            { IsRandomMove = false; }
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
        void MoveToWithLookAtNoWait(Vector3F pos, Vector3F lookAt)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.MoveWithLookAt;
            req.Pos = pos;
            req.Guid = guid;
            req.LookAt = lookAt;
            MoveQueue.Enqueue(req);
        }
        bool ComeToAndWaitStop(Vector3F pos, double dist)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.ComeTo;
            req.Pos = pos;
            req.Guid = guid;
            req.dist = dist;
            MoveQueue.Enqueue(req);

            while (MoveResults[guid] == 0)
                Thread.Sleep(10);

            var result = MoveResults[guid] == 1;
            MoveResults.Remove(guid);
            while (Me.IsMoving)
                Thread.Sleep(5);
            return result;
        }
        bool ComeToAndWaitStop(Entity obj, double dist)
        {
            var guid = Guid.NewGuid();
            MoveResults[guid] = 0;

            var req = new MoveRequest();
            req.Type = EMoveCallCmdType.ComeTo;
            req.Obj = obj;
            req.Guid = guid;
            req.dist = dist;
            MoveQueue.Enqueue(req);

            while (MoveResults[guid] == 0)
                Thread.Sleep(10);

            var result = MoveResults[guid] == 1;
            MoveResults.Remove(guid);
            while (Me.IsMoving)
                Thread.Sleep(5);
            return result;
        }
        DateTime RandomMovesNextDirChange = DateTime.UtcNow;
        void UpdateRandomMoveTimes()
        {
            if (RandomMovesNextDirChange < DateTime.UtcNow)
            {
                RandomMovesNextDirChange = DateTime.UtcNow.AddSeconds(RandomGen.Next(5, 20));
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
                        req.Pos = new Vector3F(FarmZone.X + dist2 * Math.Cos(angle), FarmZone.Y + dist2 * Math.Sin(angle), GetNavMeshHeight(FarmZone.X + dist2 * Math.Cos(angle), FarmZone.Y + dist2 * Math.Sin(angle)));
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
                NextBestPosMoveAllow = DateTime.UtcNow.AddSeconds(RandomGen.Next(4, 10));
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

            var list = AggroMobsInsideFarmZone.ToList();

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
            bool needCheck = AggroMobsOnMe.Count > 0;
            uint visibleBest = 0;
            if (!needCheck)
            {
                visibleBest = CountVisibleMobs(Me.Location, Me.Rotation.Y);
                needCheck = visibleBest < AggroMobsInsideFarmZone.Count;
            }

            if (!needCheck)
                return;


            Vector3F best = Vector3F.Zero;
            double distToBest = double.MaxValue;
            for (int i = 0; i < 50; i++)
            {
                double angle = GetRandomNumber(0, Math.PI * 2);
                var pos = new Vector3F(BestMob.Location.X + GetRandomNumber(6, 10) * Math.Cos(angle), BestMob.Location.Y + GetRandomNumber(6, 10) * Math.Sin(angle), Me.Location.Z);
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

            if (best != Vector3F.Zero)
            {
                //MoveToWithLookAtNoWait(best, BestMob.Location);
                ForceMoveToWithLookTo(best, BestMob.Location);
            }
        }
        void InitClassCfg()
        {
            if (Me.Class == EClass.Monk && Me.TalentSpecId == 268)
                ClassCfg = new MonkBrewmasterClassConfig();
            else if (Me.Class == EClass.Druid && Me.TalentSpecId == 102)
                ClassCfg = new DruidBalance();
            else if (Me.Class == EClass.Shaman && Me.TalentSpecId == 262)
                ClassCfg = new ShamanElemental();
            else if (Me.Class == EClass.Hunter && Me.TalentSpecId == 254)
                ClassCfg = new HunterMarkshman();
            else
            {
                Log("Unknown ClassSpec: " + Me.Class + "[" + Me.TalentSpecId + "]");
                ClassCfg = new ClassConfig();
            }
        }
        
        void SetupVariables()
        {
            Totem = null;
            AggroMobsAll.Clear();
            AggroMobsOnParty.Clear();
            AggroMobsOnMe.Clear();
            AggroMobsInsideFarmZone.Clear();
            GroupPlayers.Clear();
            LootableMobs.Clear();
            MobsCanBePulled.Clear();
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
                AggroMobsAll.Add(t.Obj);
                AggroMobsOnMe.Add(t.Obj);
            }
            foreach (var mob in mobs)
            {
                if (FarmCfg.MobIDs.Contains(mob.Id))
                {
                    if (mob.IsAlive)
                    {
                        if (Totem != null && mob.Target == Totem)
                            AggroMobsAll.Add(mob);
                        foreach (var p in GroupPlayers)
                        {
                            if (mob.Target == p)
                            {
                                AggroMobsAll.Add(mob);
                                AggroMobsOnParty.Add(mob);
                            }
                        }
                    }
                    if (!mob.IsAlive && mob.Distance(Totem) < 15)
                    {
                        if (mob.IsLootable)
                            LootableMobs.Add(mob);
                    }
                }
            }
            foreach (var mob in AggroMobsAll)
            {
                if (FarmZone.ObjInZone(mob))
                    AggroMobsInsideFarmZone.Add(mob);
            }
            foreach (var mob in mobs)
            {
                if (FarmCfg.MobIDs.Contains(mob.Id) && mob.IsAlive
                    && !AggroMobsAll.Exists(b => b == mob) && mob.TargetGuid == WowGuid.Zero
                    && 
                    (       (FarmCfg.PullRoundZone != null && FarmCfg.PullRoundZone.ObjInZone(mob)) 
                        ||  (FarmCfg.PullPolygoneZone != null && FarmCfg.PullPolygoneZone.ObjInZone(mob))
                    )
                    )
                    MobsCanBePulled.Add(mob);
            }
            BestMob = GetBestMob();
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
                            {
                                ComeToAndWaitStop(p, Math.Max(0.5f, spellCastRange - 2));
                            }
                            if (UseSingleSpell(spellData.Id, !spellInstant, p))
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
                    if (UseSingleSpell(spellData.Id, !spellInstant, Me))
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
                    if (UseSingleSpell(spellData.Id, !spellInstant, Me))
                        return true;
                }
            }
            return false;
        }
        bool UseTaunt()
        {
            if (ClassCfg.TauntSpellId != 0 && AggroMobsOnParty.Count > 0)
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
                if (UseSingleSpell(ClassCfg.TauntSpellId, !spellInstant, mob))
                    return true;
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
                var castPoint = new Vector3F(randPoint2D.X, randPoint2D.Y, GetNavMeshHeight(randPoint2D.X, randPoint2D.Y));
                if (spellCastRange != 0 && spellCastRange < Me.Distance(castPoint))
                    ComeToAndWaitStop(castPoint, Math.Max(0.5f, spellCastRange - 2));
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                {
                    CancelMovesAndWaitStop();
                }
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
        bool CheckDistToFarmZone()
        {
            if (NextCheckDist > DateTime.UtcNow)
                return false;
            NextCheckDist = DateTime.UtcNow.AddSeconds(RandomGen.Next(13, 31));
            var dist = FarmZone.Radius;
            if (ClassCfg.RandomMovesType == ERandomMovesType.MidRange1 || ClassCfg.RandomMovesType == ERandomMovesType.MidRange2)
                dist = 23;
            if (Me.Distance2D(new Vector3F(FarmZone.X, FarmZone.Y, 0)) < dist)
            {
                var posToCome = new Vector3F(FarmZone.X, FarmZone.Y, GetNavMeshHeight(FarmZone.X, FarmZone.Y));
                ComeToAndWaitStop(posToCome, Math.Max(0.5f, dist - RandomGen.Next(2, 5)));
                return true;
            }
            return false;
        }
        Unit GetBestMobForPull()
        {
            Unit result = null;
            double dist = double.MaxValue;
            foreach (var mob in MobsCanBePulled)
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
            foreach (var mob in AggroMobsOnParty)
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
            double dist = double.MaxValue;
            foreach (var mob in AggroMobsInsideFarmZone)
            {
                if (dist > Me.Distance(mob))
                {
                    result = mob;
                    dist = Me.Distance(mob);
                }
            }
            if (result == null)
            {
                foreach (var mob in AggroMobsAll)
                {
                    if (dist > Me.Distance(mob))
                    {
                        result = mob;
                        dist = Me.Distance(mob);
                    }
                }
            }
            return result;
        }
        bool PullMobs()
        {
            if (ClassCfg.PullSpellId != 0 && MobsCanBePulled.Count > 0)
            {
                var mob = GetBestMobForPull();
                if (mob == null)
                    return false;
                var spellInstant = IsSpellInstant(ClassCfg.PullSpellId);
                var spellCastRange = Math.Max(0, GetSpellCastRange(ClassCfg.PullSpellId) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(mob))
                    ComeToAndWaitStop(mob, Math.Max(0.5f, spellCastRange - 2));
                if (!spellInstant && (Me.IsMoving || MoveQueue.Count > 0))
                {
                    CancelMovesAndWaitStop();
                }
                if (UseSingleSpell(ClassCfg.PullSpellId, !spellInstant, mob))
                    return true;
            }
            return false;
            
        }
        bool UseAttackSpells()
        {
            if (BestMob == null)
                return false;
            if (ClassCfg.PullSpellId != 0 && MobsCanBePulled.Count > 0)
                return false;

            foreach (var spell in ClassCfg.AttackSpellIds)
            {
                var spellInstant = IsSpellInstant(spell.Id);
                var spellCastRange = Math.Max(0, GetSpellCastRange(spell.Id) - 1);
                if (spellCastRange != 0 && spellCastRange < Me.Distance(BestMob))
                    ComeToAndWaitStop(BestMob, Math.Max(0.5f, spellCastRange - 2));

                bool spellCanMoveWhileCasting = false;
                //пока так, потом надо функцию в АПИ
                if (spell.Id == 120360 || spell.Id == 257044 || spell.Id == 56641)
                {
                    spellCanMoveWhileCasting = true;
                }
                if (UseSingleSpell(spell.Id, !spellInstant && !spellCanMoveWhileCasting, BestMob, spell.SendLocation ? BestMob.Location : new Vector3F()))
                    return true;
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
        bool UseSingleSpell(uint id, bool waitCasts, Unit target = null, Vector3F pos = new Vector3F())
        {
            if (waitCasts && (Me.IsMoving || MoveQueue.Count > 0))
            {
                CancelMovesAndWaitStop();
            }
            if (target != null && target != Me && Me.Target != target)
                SetTarget(target);
            //TurnIfNeed(target, false);
            var crPre = SpellManager.CheckCanCast(id, target);
            if (crPre == ESpellCastError.UNIT_NOT_INFRONT)
                TurnIfNeed(target, true);
            var cr = SpellManager.CastSpell(id, target, pos);
            if (cr == ESpellCastError.UNIT_NOT_INFRONT)
                TurnIfNeed(target, true);
            if (cr == ESpellCastError.SUCCESS)
            {
                if (waitCasts)
                    WaitCasts();
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
                    ComeToAndWaitStop(first, maxRange);
                    Thread.Sleep(111);
                }
                if (!OpenLoot(first))
                    Log("Failed to open loot: " + GetLastError());
                else
                {
                    Console.WriteLine();
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
            foreach (var item in ItemManager.GetItems())
            {
                if (item.Place >= EItemPlace.InventoryItem && item.Place <= EItemPlace.Bag4)
                {
                    if (item.ItemQuality == EItemQuality.Uncommon)
                    {
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
                if (cond.Type == EValueType.HpPercent)
                {
                    if (!ConditionCompare(cond.Comparsion, target.HpPercents, cond.Value))
                        return false;
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
        
        public void PluginStop()
        {
            //DisableRandomJumpsOnMoves();
            MoveForward(false);
            MoveBackward(false);
            StrafeRight(false);
            StrafeLeft(false);
            Ascend(false);
            Descend(false);
            SetMoveStateForClient(false);
            CanWork = false;
        }
    }
}
