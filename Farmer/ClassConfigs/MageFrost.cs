using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.ClassConfigs
{
    public class MageFrost : ClassConfig
    {
        internal MageFrost()
        {
            RandomMovesType = ERandomMovesType.MidRange2;
            WeaponType = new List<EItemSubclassWeapon>() { EItemSubclassWeapon.STAFF };
            ArmorType = new List<EItemSubclassArmor>() { EItemSubclassArmor.CLOTH, EItemSubclassArmor.MISCELLANEOUS };

            //Стылая кровь
            BuffSpellIds.Add(new SpellCastData(12472));
            //Ice barrier
            BuffSpellIds.Add(new SpellCastData(11426));
            //Ice Floes
            BuffSpellIds.Add(new SpellCastData(108839));

            //Blizzard
            AttackSpellIds.Add(new SpellCastData(190356)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                SendLocation = true,
                IsInstaForAoeFarm = true
            });

            //Frozen Orb
            AttackSpellIds.Add(new SpellCastData(84714)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                }
            });

            //Ice Lance
            AttackSpellIds.Add(new SpellCastData(30455));
            //Flurry
            //AttackSpellIds.Add(new SpellCastData(44614));
        }
    }
}
