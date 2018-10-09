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


            //Тотем жидкой магмы
            AttackSpellIds.Add(new SpellCastData(192222)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                SendLocation = true
            });
            //Fire elemental
            AttackSpellIds.Add(new SpellCastData(198067));

            //Flame shock
            AttackSpellIds.Add(new SpellCastData(188389));
            //Earth shock
            AttackSpellIds.Add(new SpellCastData(8042));
            //Chain lighting
            AttackSpellIds.Add(new SpellCastData(188443));

        }
    }
}

