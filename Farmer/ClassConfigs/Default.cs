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
        CreaturesCount,
        MinionExists,
    }
    public class ConditionData
    {
        public EValueType Type;
        public int Value;
        public int Value2;
        public EComparsion Comparsion;
        public ConditionData(EValueType type, int value, EComparsion comparsion)
        {
            Type = type;
            Value = value;
            Comparsion = comparsion;
        }
        public ConditionData(EValueType type, int value, int value2, EComparsion comparsion)
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
        public bool IsInstaForAoeFarm; // для пулеров, по возможности кастовать в зону фарма, если там есть мобы
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
        public List<EItemSubclassWeapon> WeaponType;
        public List<EItemSubclassArmor> ArmorType;
        public EShapeshiftForm RequiredShapeshift;
        public uint ShapeshiftSpellId;
        public uint PullSpellId;
        public uint TauntSpellId;
        public uint TotemSpellId;
        public List<SpellCastData> ResSpellIds = new List<SpellCastData>(); //только которые можно юзать в бою!
        public uint SpellcastPreventSpellId;
        public List<SpellCastData> SelfHealSpellIds = new List<SpellCastData>();
        public List<SpellCastData> PartyHealSpellIds = new List<SpellCastData>();
        public List<SpellCastData> BuffSpellIds = new List<SpellCastData>();
        public List<SpellCastData> AttackSpellIds = new List<SpellCastData>();
        public uint RessurectSpellId;
        public ERandomMovesType RandomMovesType;
    }
}
