using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.ClassConfigs
{
    public enum EComparsion
    {
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }
    public enum EValueType
    {
        HpPercent,
        TargetInFarmZone,
        MonkStagger,
    }
    public class ConditionData
    {
        public EValueType Type;
        public int Value;
        public EComparsion Comparsion;
        public ConditionData(EValueType type, int value, EComparsion comparsion)
        {
            Type = type;
            Value = value;
            Comparsion = comparsion;
        }
    }
    public class SpellCastData
    {
        public uint Id;
        public bool SendLocation;
        public List<ConditionData> Conditions = new List<ConditionData>();
        public SpellCastData(uint id)
        {
            Id = id;
        }
    }
    public enum ERandomMovesType
    {
        None,
        Melee,
        MidRange1,
        MidRange2,
        MaxRange
    }
    public class ClassConfig
    {
        public EShapeshiftForm RequiredShapeshift;
        public uint ShapeshiftSpellId;
        public uint PullSpellId;
        public uint TauntSpellId;
        public uint TotemSpellId;
        public List<SpellCastData> SelfHealSpellIds = new List<SpellCastData>();
        public List<SpellCastData> PartyHealSpellIds = new List<SpellCastData>();
        public List<SpellCastData> BuffSpellIds = new List<SpellCastData>();
        public List<SpellCastData> AttackSpellIds = new List<SpellCastData>();
        public uint RessurectSpellId;
        public ERandomMovesType RandomMovesType;
    }
}
