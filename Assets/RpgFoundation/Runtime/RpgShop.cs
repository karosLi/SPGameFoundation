using Unity.Mathematics;

namespace RpgFoundation
{
    public struct ShopOffer
    {
        public ItemKind Kind;     // Potion or Gear
        public int Value;         // gear id for gear
        public int Price;
    }

    /// <summary>
    /// The merchant between floors (floor-clear screen): a potion (repeatable), armour and a weapon of
    /// the hero's family one tier above what the floor drops. Gear offers sell once per floor.
    /// </summary>
    public static class RpgShop
    {
        public const int OfferCount = 3;

        public static ShopOffer Offer(RpgRuntimeConfig config, HeroProfile profile, int index)
        {
            int floor = math.max(profile.Floor, 1);
            int tier = math.clamp((floor + 1) / 2 + 1, 1, config.Loot.GearTiers);
            switch (index)
            {
                case 0:
                    return new ShopOffer { Kind = ItemKind.Potion, Value = 1, Price = 20 + 5 * floor };
                case 1:
                    return new ShopOffer { Kind = ItemKind.Gear, Value = config.ArmourId(tier), Price = 55 * tier };
                default:
                {
                    var family = config.HeroWeapon(profile.Weapon);
                    int id = config.GearId(family, tier);
                    if (id == 0) id = config.GearId(WeaponKind.Sword, tier);
                    return new ShopOffer { Kind = ItemKind.Gear, Value = id, Price = 65 * tier };
                }
            }
        }

        public static bool Sold(int boughtMask, int index) => index > 0 && (boughtMask & (1 << index)) != 0;
    }
}
