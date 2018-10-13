using Out.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.FarmConfig
{
    public enum EPullСomplexity
    {
        Safe,
        Mid,
        Max
    }
    [Serializable]
    public class FarmConfigDefault
    {
        public Dictionary<uint, uint> MobIDs;
        //public RoundZone TotemInstallZone = new RoundZone(-1828.76, -1052.18, 2);
        public RoundZone TotemInstallZone;
        public EPullСomplexity PullСomplexity;
        public RoundZone PullRoundZone = null;
        public PolygonZone PullPolygoneZone = null;
        public float FarmZoneRadius;
        public bool DisenchantItems = true;
        public bool DeleteTrashItems = true;
        public bool LogsEnabled = false;
        public bool AutoEquipItems = false;
        public bool ProtectPullers = false; //бить мобов висящих на пати, которые вне ренжи нашей атаки?
        public string Repairman;
        public int DontPullWhenXMobsInFarmZone = 3;
        public Vector3F RepairSummonPoint;
        public uint RepairmanMountSpellId;
    }
}
