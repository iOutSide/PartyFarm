using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.ClassConfigs
{
    public class DruidBalance : ClassConfig
    {
        internal DruidBalance()
        {
            //RandomMovesType = ERandomMovesType.MidRange2;
            WeaponType = new List<EItemSubclassWeapon>() { EItemSubclassWeapon.STAFF };
            ArmorType = new List<EItemSubclassArmor>() { EItemSubclassArmor.LEATHER, EItemSubclassArmor.MISCELLANEOUS };
            PullSpellId = 93402; //sunfire
            RequiredShapeshift = EShapeshiftForm.MoonkinForm;
            ShapeshiftSpellId = 24858;

            //Regrowth
            SelfHealSpellIds.Add(new SpellCastData(8936)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 75, EComparsion.LessOrEqual)
                }
            });
            //Regrowth
            PartyHealSpellIds.Add(new SpellCastData(8936)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 60, EComparsion.LessOrEqual)
                }
            });

            ResSpellIds.Add(new SpellCastData(20484));

            //Warrior of elune
            BuffSpellIds.Add(new SpellCastData(202425));
            //Celestial aligment
            BuffSpellIds.Add(new SpellCastData(194223));


            //Aoe starfall
            AttackSpellIds.Add(new SpellCastData(191034)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                SendLocation = true,
                IsInstaForAoeFarm = true
            });
            //Fury of elune
            AttackSpellIds.Add(new SpellCastData(202770)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.TargetInFarmZone, 0, EComparsion.Equal)
                },
                IsInstaForAoeFarm = true
            });

            AttackSpellIds.Add(new SpellCastData(194153));//Lunar strike
        }
    }
}
