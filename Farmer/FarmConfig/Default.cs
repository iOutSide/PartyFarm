using Out.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.FarmConfig
{
    [Serializable]
    public class FarmConfigDefault
    {
        public List<uint> MobIDs = new List<uint>() { 135858, 137849, 141521 };
        public RoundZone TotemInstallZone = new RoundZone(-1828.76, -1052.18, 2);
        public RoundZone PullRoundZone = null;
        public PolygonZone PullPolygoneZone = null;
        public float FarmZoneRadius = 10f;
        public bool DisenchantItems = true;
        public bool DeleteTrashItems = true;
        public string Repairman = "Monsha";
        public Vector3F RepairSummonPoint = new Vector3F(-1826.80, -1038.84, 5.33);
        public uint RepairmanMountSpellId = 61447;
    }
}
