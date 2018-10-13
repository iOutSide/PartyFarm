using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.ClassConfigs
{
    class HunterMarkshman : ClassConfig
    {
        internal HunterMarkshman()
        {
            PullSpellId = 5116;
            SpellcastPreventSpellId = 147362;
            RandomMovesType = ERandomMovesType.MidRange2;
            WeaponType = new List<EItemSubclassWeapon>() { EItemSubclassWeapon.BOW, EItemSubclassWeapon.GUN };
            ArmorType = new List<EItemSubclassArmor>() { EItemSubclassArmor.MAIL, EItemSubclassArmor.MISCELLANEOUS };
        
            //Exhilaration
            SelfHealSpellIds.Add(new SpellCastData(109304)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 55, EComparsion.LessOrEqual)
                }
            });


            //Trueshot
            BuffSpellIds.Add(new SpellCastData(193526));
            //Survival of the Fittest
            BuffSpellIds.Add(new SpellCastData(264735));



            //Barrage
            AttackSpellIds.Add(new SpellCastData(120360));
            //Rapid Fire
            AttackSpellIds.Add(new SpellCastData(257044));
            //Multi-Shot
            AttackSpellIds.Add(new SpellCastData(257620));
            //Steady shot
            AttackSpellIds.Add(new SpellCastData(56641));

        }
    }
}

