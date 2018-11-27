using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWBot.Core;

namespace PartyFarm.ClassConfigs
{
    public class MonkBrewmasterClassConfig : ClassConfig
    {
        internal MonkBrewmasterClassConfig()
        {
            RandomMovesType = ERandomMovesType.Melee;
            WeaponType = new List<EItemSubclassWeapon>() { EItemSubclassWeapon.STAFF };
            ArmorType = new List<EItemSubclassArmor>() { EItemSubclassArmor.LEATHER, EItemSubclassArmor.MISCELLANEOUS };
            TotemSpellId = 115315;
            TauntSpellId = 115546;

            //Целебный эликсир
            SelfHealSpellIds.Add(new SpellCastData(122281)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 65, EComparsion.LessOrEqual)
                }
            });

            //Vivify
            SelfHealSpellIds.Add(new SpellCastData(116670)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.HpPercent, 50, EComparsion.LessOrEqual)
                }
            });

            //Очищающий отвар
            BuffSpellIds.Add(new SpellCastData(119582)
            {
                Conditions = new List<ConditionData> {
                    new ConditionData(EValueType.MonkStagger, 1000, EComparsion.Greater)
                }
            });


            AttackSpellIds.Add(new SpellCastData(100784));//нокаутирующая атака
            AttackSpellIds.Add(new SpellCastData(119381));//Круговой удар ногой
            AttackSpellIds.Add(new SpellCastData(116847));//порыв нефритового ветра
            AttackSpellIds.Add(new SpellCastData(121253));//удар боченком
            AttackSpellIds.Add(new SpellCastData(115181));//пламенное дыхание
            AttackSpellIds.Add(new SpellCastData(100780));//лапа тигра
        }
    }
}
