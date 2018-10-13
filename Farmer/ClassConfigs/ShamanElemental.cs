using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PartyFarm.ClassConfigs
{
    class ShamanElemental : ClassConfig
    {
        internal ShamanElemental()
        {
            PullSpellId = 196840;
            SpellcastPreventSpellId = 57994;
            RandomMovesType = ERandomMovesType.MidRange2;
            //Исцеляющий всплеск
            SelfHealSpellIds.Add(new SpellCastData(8004)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 75, EComparsion.LessOrEqual)
                }
            });
            //Исцеляющий всплеск
            PartyHealSpellIds.Add(new SpellCastData(8004)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 50, EComparsion.LessOrEqual)
                }
            });

            //StormKeeper
            BuffSpellIds.Add(new SpellCastData(191634));
            //Bloodlust
            BuffSpellIds.Add(new SpellCastData(2825));
            //Totem Mastery
            BuffSpellIds.Add(new SpellCastData(210643) {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.CreaturesCount, 106317, 0, EComparsion.Equal)
                }
            });
            //Fire elemental
            BuffSpellIds.Add(new SpellCastData(198067)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.MinionExists, 0, EComparsion.Equal)
                }
            });
            //Earth elemental
            BuffSpellIds.Add(new SpellCastData(198103)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.MinionExists, 0, EComparsion.Equal)
                }
            });

            //Тотем жидкой магмы
            AttackSpellIds.Add(new SpellCastData(192222)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                SendLocation = true
            });
            /*//Fire elemental
            AttackSpellIds.Add(new SpellCastData(198067)
            {
                SendLocation = true
            });*/

            //Flame shock
            AttackSpellIds.Add(new SpellCastData(188389));
            //Earth shock
            //AttackSpellIds.Add(new SpellCastData(8042));
            AttackSpellIds.Add(new SpellCastData(61882)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                SendLocation = true
            });
            //Chain lighting
            AttackSpellIds.Add(new SpellCastData(188443));

        }
    }
}

