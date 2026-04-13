using ExpandedStomach;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace ExpandedStomach.HarmonyPatches
{
    public static class HarmonyPatchesVars
    {
        public static bool BrainFreezeInstalled = false;
        public static bool IthaniaCannedGoodsInstalled = false;
    }

    public static class ServerPatcher
    {
        /// <summary>
        /// Registers all Harmony prefix/postfix patches for the server side.
        /// Called once at mod startup after compatibility flags in <see cref="HarmonyPatchesVars"/> have been set.
        /// </summary>
        public static void ApplyServerPatches(Harmony harmony)
        {
            // tryFinishEatMeal (BlockMeal → expanded stomach overflow after bowl meals)
            var tryFEM = AccessTools.Method(typeof(BlockMeal), "tryFinishEatMeal");
            if (tryFEM == null)
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find BlockMeal.tryFinishEatMeal — meal overflow patch skipped.");
            else
                harmony.Patch(tryFEM,
                    prefix:  new HarmonyMethod(AccessTools.Method(typeof(YeahBoiScrapeThatBowl), nameof(YeahBoiScrapeThatBowl.Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(YeahBoiScrapeThatBowl), nameof(YeahBoiScrapeThatBowl.Postfix))));

            // tryEatStop (BlockLiquidContainerBase → expanded stomach overflow after drinking)
            var tryeatstopLiquid = AccessTools.Method(typeof(BlockLiquidContainerBase), "tryEatStop");
            if (tryeatstopLiquid == null)
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find BlockLiquidContainerBase.tryEatStop — liquid overflow patch skipped.");
            else
                harmony.Patch(tryeatstopLiquid,
                    prefix:  new HarmonyMethod(AccessTools.Method(typeof(DrinkUpMyFriend), nameof(DrinkUpMyFriend.Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(DrinkUpMyFriend), nameof(DrinkUpMyFriend.Postfix))));

            // tryEatStop (CollectibleObject → expanded stomach overflow after eating food items)
            var tryeatstopCO = AccessTools.Method(typeof(CollectibleObject), "tryEatStop");
            if (tryeatstopCO == null)
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find CollectibleObject.tryEatStop — food item overflow patch skipped.");
            else
                harmony.Patch(tryeatstopCO,
                    prefix:  new HarmonyMethod(AccessTools.Method(typeof(OmNomNomNomFooooood), nameof(OmNomNomNomFooooood.Prefix)))  { priority = Priority.Last },
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(OmNomNomNomFooooood), nameof(OmNomNomNomFooooood.Postfix))) { priority = Priority.Last });

            // updateBodyTemperature (EntityBehaviorBodyTemperature → fat-based temperature resistance)
            var EBBTonGameTick = AccessTools.Method(typeof(EntityBehaviorBodyTemperature), "updateBodyTemperature");
            if (EBBTonGameTick == null)
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find EntityBehaviorBodyTemperature.updateBodyTemperature — fat temperature patch skipped.");
            else
                harmony.Patch(EBBTonGameTick,
                    prefix:  new HarmonyMethod(AccessTools.Method(typeof(Patch_EntityBehaviorBodyTemperature_UpdateBodyTemperature), nameof(Patch_EntityBehaviorBodyTemperature_UpdateBodyTemperature.Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(Patch_EntityBehaviorBodyTemperature_UpdateBodyTemperature), nameof(Patch_EntityBehaviorBodyTemperature_UpdateBodyTemperature.Postfix))));

            // ReduceSaturation (EntityBehaviorHunger → drain expanded stomach before base saturation)
            var EBHRedSat = AccessTools.Method(typeof(EntityBehaviorHunger), "ReduceSaturation");
            if (EBHRedSat == null)
                ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find EntityBehaviorHunger.ReduceSaturation — expanded stomach drain patch skipped.");
            else
                harmony.Patch(EBHRedSat,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(Patch_EntityBehaviorHunger_ReduceSaturation), nameof(Patch_EntityBehaviorHunger_ReduceSaturation.Prefix))) { priority = Priority.Last });

            // Ithania Canned Goods
            if (HarmonyPatchesVars.IthaniaCannedGoodsInstalled)
            {
                // CollectibleObject.OnHeldUseStop is a non-virtual vanilla method that the engine
                // calls as the interaction-stop entry point. It then calls OnHeldInteractStop via
                // virtual dispatch. ICG does NOT override OnHeldUseStop, so this vanilla body
                // always executes, giving us a reliable pre/post that wraps EatFromCan entirely.
                var onHeldUseStop = AccessTools.Method(typeof(CollectibleObject), "OnHeldUseStop");
                if (onHeldUseStop == null)
                {
                    ExpandedStomachModSystem.Logger.Error("ExpandedStomach: Could not find CollectibleObject.OnHeldUseStop — ICG compatibility patch skipped.");
                }
                else
                {
                    harmony.Patch(onHeldUseStop,
                        prefix:  new HarmonyMethod(AccessTools.Method(typeof(CANweEatIThania), nameof(CANweEatIThania.Prefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(CANweEatIThania), nameof(CANweEatIThania.Postfix))));
                    ExpandedStomachModSystem.Logger.Notification("ExpandedStomach: IthaniaCannedGoods compatibility patch applied successfully.");
                }
            }
        }
    }

    #region Ithania
    // Wraps CollectibleObject.OnHeldUseStop — a non-virtual vanilla method that the engine calls
    // as the interaction-stop entry point for ALL collectibles. ICG does not override it, so the
    // vanilla body runs unconditionally and then calls ItemCannedFood.OnHeldInteractStop (and
    // therefore EatFromCan) via virtual dispatch.
    //
    // We guard on the slot being an ICG can-opened-* item so the overhead on non-ICG interactions
    // is a single null/string check and an early return.
    //
    // Serving-count comparison strategy:
    //   servings unchanged after call  →  EatFromCan returned early (base full); eat 1 serving into expanded stomach
    //   servings decreased             →  EatFromCan ate partial; eat the overflow fraction into expanded stomach
    //   servings == 0 / slot empty     →  EatFromCan ate everything; nothing to do
    public static class CANweEatIThania
    {
        public struct State
        {
            public float ServingsBefore;
            public float SatPerServing;
            /// <summary>Nutrition breakdown of each ingredient for per-category crediting in the postfix.</summary>
            public FoodNutritionProperties[] IngredientProps;
            /// <summary>Ingredient with the highest satiety, used for the ReceiveSaturation event call.</summary>
            public EnumFoodCategory DominantCategory;
        }

        /// <summary>
        /// Snapshot the can state before <c>EatFromCan</c> runs: servings remaining, total satiety per
        /// serving, and the per-ingredient nutrition breakdown for accurate per-category crediting.
        /// </summary>
        public static void Prefix(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
                                  EnumHandInteract useType, ref State __state)
        {
            try
            {
                __state = default;

                // Only handle interact (not attack) on the server side.
                if (useType != EnumHandInteract.HeldItemInteract) return;
                if (byEntity.World.Side != EnumAppSide.Server) return;
                if (byEntity.Controls.Sneak) return;
                if (secondsPassed < 1.9f) return;
                var code = slot?.Itemstack?.Collectible?.Code;
                if (code?.Domain != "ithaniacannedgoods" || code.Path?.StartsWith("can-opened") != true) return;

                float servings = slot.Itemstack.Attributes.GetFloat("quantityServings");
                if (servings <= 0f) return;

                // Record satiety before eating so the rest of ES can reference it.
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                if (stomach == null || hunger == null) return;

                float satBefore = hunger.GetFloat("currentsaturation");
                stomach.SetFloat("satietyBeforeEating", satBefore);
                byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

                // Collect per-ingredient nutrition props for accurate per-category overflow crediting.
                if (!(slot.Itemstack.Attributes["contents"] is ITreeAttribute contents)) return;

                float satPerServing = 0f;
                float dominantSat   = 0f;
                var   dominant      = EnumFoodCategory.Unknown;
                var   propsList     = new List<FoodNutritionProperties>();

                foreach (var kvp in contents)
                {
                    if (!(kvp.Value is ItemstackAttribute { value: { } stack })) continue;
                    stack.ResolveBlockOrItem(byEntity.World);
                    var props = BlockMeal.GetIngredientStackNutritionProperties(byEntity.World, stack, null);
                    if (props == null) continue;
                    satPerServing += props.Satiety;
                    if (props.Satiety > dominantSat) { dominantSat = props.Satiety; dominant = props.FoodCategory; }
                    propsList.Add(props);
                }

                if (satPerServing <= 0f || propsList.Count == 0) return;

                __state.ServingsBefore   = servings;
                __state.SatPerServing    = satPerServing;
                __state.DominantCategory = dominant;
                __state.IngredientProps  = propsList.ToArray();
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"ICG Prefix threw: {ex}");
            }
        }

        /// <summary>
        /// After <c>EatFromCan</c> returns, absorb any overflow (base was full or partially full)
        /// into the expanded stomach and credit each ingredient's food category proportionally.
        /// </summary>
        public static void Postfix(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
                                   EnumHandInteract useType, State __state)
        {
            try
            {
                if (useType != EnumHandInteract.HeldItemInteract) return;
                if (byEntity.World.Side != EnumAppSide.Server) return;
                if (byEntity.Controls.Sneak) return;
                if (secondsPassed < 1.9f) return;
                if (__state.IngredientProps == null || __state.SatPerServing <= 0f) return;

                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (stomach == null) return;

                // Determine what EatFromCan did to the slot.
                bool slotIsEmpty  = slot?.Itemstack == null
                                    || slot.Itemstack.Collectible?.Code?.Path == "can-empty";
                float servingsNow = slotIsEmpty
                                    ? 0f
                                    : slot!.Itemstack.Attributes.GetFloat("quantityServings");

                // ICG caps each eat interaction at 1 serving (Math.Min(1f, num) in EatFromCan).
                // We mirror that cap here so expanded stomach also only absorbs up to 1 serving
                // worth of overflow per interaction, regardless of how many servings the can holds.
                float portionBefore   = Math.Min(1f, __state.ServingsBefore);
                float baseConsumed    = __state.ServingsBefore - servingsNow;
                float expandedPortion = portionBefore - baseConsumed;

                if (expandedPortion <= 0.001f) return;

                int   stomachSize      = stomach.GetInt("stomachSize");
                float stomachMeter     = stomach.GetFloat("expandedStomachMeter");
                float stomachAvailable = (float)stomachSize - stomachMeter;
                if (stomachAvailable <= 0f) return;

                float overflowSat   = expandedPortion * __state.SatPerServing;
                float satToAbsorb   = Math.Min(overflowSat, stomachAvailable);
                float servingsToEat = satToAbsorb / __state.SatPerServing;

                float newServings = servingsNow - servingsToEat;
                if (newServings <= 0.001f)
                {
                    Item emptyCan = byEntity.World.GetItem(new AssetLocation("ithaniacannedgoods", "can-empty"));
                    slot!.Itemstack = emptyCan != null ? new ItemStack(emptyCan) : null;
                }
                else
                {
                    slot!.Itemstack!.Attributes.SetFloat("quantityServings", newServings);
                }
                slot!.MarkDirty();

                stomach.SetFloat("expandedStomachMeter", stomachMeter + satToAbsorb);
                byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

                // Credit each ingredient's category proportionally to the servings absorbed.
                foreach (var prop in __state.IngredientProps)
                {
                    float ingredientSat = prop.Satiety * servingsToEat;
                    if (ingredientSat > 0.001f)
                        Helpers.GetNutrientsFromFoodType(prop.FoodCategory, ingredientSat, byEntity);
                }
                byEntity.ReceiveSaturation(0f, __state.DominantCategory);
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"ICG Postfix threw: {ex}");
            }
        }
    }
    #endregion

    #region BlockMeal_TryFinishEatMeal
    // Postfix on BlockMeal.tryFinishEatMeal.
    // When the base hunger meter is full and the meal still has servings remaining after the
    // vanilla Consume() call, we absorb as many of those servings as the expanded stomach can
    // hold in one go, then update the slot and credit nutrients accordingly.
    public static class YeahBoiScrapeThatBowl
    {
        public struct State
        {
            public float ServingsBefore;
            public float SatPerServing;
            public float SatBefore;
            public float MaxSat;
            public bool  Valid;
            public bool  EatAllInOneGo;
            public FoodNutritionProperties[] MultiProps;
        }

        /// <summary>
        /// Snapshot the meal state before <c>tryFinishEatMeal</c> runs: servings, satiety per serving,
        /// current and max saturation, and whether the combined base+expanded capacity can hold the
        /// whole meal in one interaction (<see cref="State.EatAllInOneGo"/>).
        /// </summary>
        public static void Prefix(BlockMeal __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity,
                                   ref State __state)
        {
            try
            {
                __state = default;

                if (byEntity.World.Side != EnumAppSide.Server) return;
                if (secondsUsed < 1.45f) return;
                if (slot?.Itemstack == null) return;

                FoodNutritionProperties[] multiProps = __instance.GetContentNutritionProperties(byEntity.World, slot, byEntity);
                if (multiProps == null || multiProps.Length == 0) return;

                float satPerServing = 0f;
                foreach (var prop in multiProps)
                    satPerServing += prop.Satiety;
                if (satPerServing <= 0f) return;

                float servingsBefore = slot.Itemstack.Attributes.GetFloat("quantityServings");
                if (servingsBefore <= 0f) return;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                float satBefore       = hunger.GetFloat("currentsaturation");
                float maxSat          = hunger.GetFloat("maxsaturation");
                float baseHeadroom    = maxSat - satBefore;
                float expandedAvail   = stomach.GetInt("stomachSize") - stomach.GetFloat("expandedStomachMeter");
                float totalMealSat    = servingsBefore * satPerServing;

                __state = new State
                {
                    ServingsBefore = servingsBefore,
                    SatPerServing  = satPerServing,
                    SatBefore      = satBefore,
                    MaxSat         = maxSat,
                    Valid          = true,
                    // True only when base headroom + expanded capacity can hold the entire meal.
                    // When false and base isn't yet full, the postfix skips overflow so the
                    // player isn't silently stuffed into the expanded stomach on a partial fill.
                    EatAllInOneGo  = (baseHeadroom + expandedAvail) >= totalMealSat,
                    MultiProps     = multiProps
                };
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"YeahBoiScrapeThatBowl Prefix threw: {ex}");
            }
        }

        /// <summary>
        /// After <c>tryFinishEatMeal</c> returns, absorb remaining servings into the expanded stomach
        /// when the base is full (or the whole meal fits in one go). Credits each meal component's
        /// food category proportionally via <see cref="Helpers.GetNutrientsFromMeal"/>.
        /// </summary>
        public static void Postfix(BlockMeal __instance, ItemSlot slot, EntityAgent byEntity,
                                    bool handleAllServingsConsumed, bool __result, State __state)
        {
            try
            {
                if (!__result || !__state.Valid) return;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                // Only absorb overflow when:
                //   (a) the base was already full before this interaction, or
                //   (b) combined headroom was enough to eat the whole meal in one go.
                // Case (b) handles the smooth "finish the bowl" path.
                // Without one of these conditions, the player is only partially filling their
                // base meter and we leave the expanded stomach alone — no accidental overeating.
                bool baseWasFull = __state.SatBefore >= __state.MaxSat - 0.1f;
                if (!baseWasFull && !__state.EatAllInOneGo) return;

                // Also require the base meter to actually be at capacity now (Consume filled it).
                float currentsatNow = hunger.GetFloat("currentsaturation");
                if (currentsatNow < __state.MaxSat - 0.1f) return;

                // After tryFinishEatMeal ran, check how many servings remain in the slot.
                float servingsNow = 0f;
                if (slot?.Itemstack?.Collectible is BlockMeal meal)
                    servingsNow = meal.GetQuantityServings(byEntity.World, slot.Itemstack);

                if (servingsNow <= 0.001f) return; // base game ate everything

                // Absorb as many remaining servings as the expanded stomach can hold.
                float stomachMeter     = stomach.GetFloat("expandedStomachMeter");
                int   stomachSize      = stomach.GetInt("stomachSize");
                float expandedAvail    = (float)stomachSize - stomachMeter;
                if (expandedAvail <= 0f) return;

                float overflowSat   = servingsNow * __state.SatPerServing;
                float satToAbsorb   = Math.Min(overflowSat, expandedAvail);
                float servingsToEat = satToAbsorb / __state.SatPerServing;
                float newServings   = servingsNow - servingsToEat;

                // Update slot.
                if (newServings <= 0.001f)
                {
                    // All remaining servings consumed — replicate tryFinishEatMeal slot cleanup.
                    IPlayer player = (byEntity as EntityPlayer)?.Player;
                    if (handleAllServingsConsumed && player != null)
                    {
                        if (__instance.Attributes["eatenBlock"].Exists)
                        {
                            Block block = byEntity.World.GetBlock(new AssetLocation(__instance.Attributes["eatenBlock"].AsString()));
                            if (block != null)
                            {
                                if (slot.Empty || slot.StackSize == 1)
                                    slot.Itemstack = new ItemStack(block);
                                else if (!player.InventoryManager.TryGiveItemstack(new ItemStack(block), slotNotifyEffect: true))
                                    byEntity.World.SpawnItemEntity(new ItemStack(block), byEntity.Pos.XYZ);
                            }
                        }
                        else
                        {
                            slot.TakeOut(1);
                        }
                    }
                    slot.MarkDirty();
                }
                else
                {
                    (__instance as BlockMeal)?.SetQuantityServings(byEntity.World, slot.Itemstack, newServings);
                    slot.MarkDirty();
                }

                // Credit expanded stomach.
                stomach.SetFloat("expandedStomachMeter", stomachMeter + satToAbsorb);
                byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

                // Credit nutrients (scaled to servings actually absorbed).
                Helpers.GetNutrientsFromMeal(__state.MultiProps, servingsToEat, byEntity);
                byEntity.ReceiveSaturation(0f, __state.MultiProps[0].FoodCategory);
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"YeahBoiScrapeThatBowl Postfix threw: {ex}");
            }
        }
    }
    //----------------------------------------------------------------------------
    #endregion
    
    #region BlockLiquidContainerBase_TryEatStop
    // Postfix on BlockLiquidContainerBase.tryEatStop.
    // Each drink interaction consumes exactly DrinkPortionSize litres (fixed, not the whole container).
    // The player made a deliberate choice to sip — so we always split that portion's satiety
    // immediately between base hunger and expanded stomach, with no gate on "drink again to unlock".
    // Overflow = expectedSat − how much room the base had — goes straight to expanded stomach.
    public static class DrinkUpMyFriend
    {
        public struct State
        {
            public float SatBefore;
            public float MaxSat;
            public float ExpectedSat;
            public EnumFoodCategory FoodCategory;
            public int   LiquidStackSizeBefore;
            public bool  Valid;
        }

        /// <summary>
        /// Snapshot the liquid state before <c>tryEatStop</c> runs: mirrors the method's own
        /// portion-size scaling to compute the exact expected satiety for this sip, and records
        /// the liquid stack size so the postfix can confirm a drink actually occurred.
        /// </summary>
        public static void Prefix(BlockLiquidContainerBase __instance, float secondsUsed, ItemSlot slot,
                                   EntityAgent byEntity, ref State __state)
        {
            try
            {
                __state = default;

                if (byEntity.World.Side != EnumAppSide.Server) return;
                if (secondsUsed < 0.95f) return;
                if (slot?.Itemstack == null || __instance.IsEmpty(slot.Itemstack)) return;

                if (!(byEntity.World is IServerWorldAccessor world)) return;

                WaterTightContainableProps containableProps = __instance.GetContentProps(slot.Itemstack);
                FoodNutritionProperties nutriProps = __instance.GetNutritionPropertiesPerLitre(world, slot.Itemstack, byEntity)?.Clone();
                if (containableProps == null || nutriProps == null) return;

                // Mirror the method's own satiety scaling to get the exact expected sat for this sip.
                float litresToDrink    = Math.Max(1f / containableProps.ItemsPerLitre, __instance.DrinkPortionSize);
                int   drinkPortionItems = (int)(litresToDrink * containableProps.ItemsPerLitre);
                float itemsPerLitreMul = (float)drinkPortionItems / containableProps.ItemsPerLitre;
                float expectedSat      = nutriProps.Satiety * itemsPerLitreMul;
                if (expectedSat <= 0f) return;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                __state = new State
                {
                    SatBefore             = hunger.GetFloat("currentsaturation"),
                    MaxSat                = hunger.GetFloat("maxsaturation"),
                    ExpectedSat           = expectedSat,
                    FoodCategory          = nutriProps.FoodCategory,
                    LiquidStackSizeBefore = __instance.GetContent(slot.Itemstack)?.StackSize ?? 0,
                    Valid                 = true
                };
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"DrinkUpMyFriend Prefix threw: {ex}");
            }
        }

        /// <summary>
        /// After <c>tryEatStop</c> returns, confirm the drink happened via liquid level drop, then
        /// absorb any satiety overflow (beyond base headroom) into the expanded stomach.
        /// Liquids with no food category (e.g. plain water) fill the expanded stomach meter but
        /// skip nutrition crediting and the <c>ReceiveSaturation</c> event.
        /// </summary>
        public static void Postfix(BlockLiquidContainerBase __instance, ItemSlot slot,
                                    EntityAgent byEntity, State __state)
        {
            try
            {
                if (!__state.Valid) return;

                // Confirm the drink actually happened — liquid level must have dropped.
                ItemStack contentAfter = slot?.Itemstack != null ? __instance.GetContent(slot.Itemstack) : null;
                int liquidNow = contentAfter?.StackSize ?? 0;
                if (liquidNow >= __state.LiquidStackSizeBefore) return;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                // How much of the sip's satiety did the base meter absorb?
                float satConsumedByBase = Math.Min(__state.MaxSat - __state.SatBefore, __state.ExpectedSat);
                float overflowSat       = __state.ExpectedSat - satConsumedByBase;
                if (overflowSat <= 0.001f) return;

                float stomachMeter  = stomach.GetFloat("expandedStomachMeter");
                int   stomachSize   = stomach.GetInt("stomachSize");
                float expandedAvail = (float)stomachSize - stomachMeter;
                if (expandedAvail <= 0f) return;

                float satToAbsorb = Math.Min(overflowSat, expandedAvail);

                stomach.SetFloat("expandedStomachMeter", stomachMeter + satToAbsorb);
                byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

                // Only credit nutrition and fire the eat event when the liquid has a real food category.
                // Water and other category-less liquids (e.g. from Hydrate or Diedrate) simply fill
                // the stomach meter without affecting nutrient levels.
                if (__state.FoodCategory != EnumFoodCategory.Unknown)
                {
                    Helpers.GetNutrientsFromFoodType(__state.FoodCategory, satToAbsorb, byEntity);
                    byEntity.ReceiveSaturation(0f, __state.FoodCategory);
                }
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"DrinkUpMyFriend Postfix threw: {ex}");
            }
        }
    }
    #endregion

    #region CollectibleObject_TryEatStop
    // Prefix/postfix on CollectibleObject.tryEatStop — covers meat, bread, berries, and any mod
    // food item that extends CollectibleObject without overriding tryEatStop.
    //
    // Health effects (nutriProps.Health → ReceiveDamage) are entirely handled by the vanilla
    // method and are never touched here; healing/poison works exactly as normal.
    //
    // BrainFreeze: BF patches tryEatStop via its own transpiler, which runs inside the method
    // body. Our prefix/postfix wraps the whole thing, so BF's code always executes unimpeded.
    // We use an analytical headroom calculation rather than a delta so we don't misattribute
    // BF's saturation adjustments as ES overflow.
    //
    // EFACA/ACA: expandedSats attributes must be read in the prefix because the vanilla method
    // calls slot.TakeOut(1) before returning, leaving the slot empty by postfix time.
    // Each additive nutrition category is credited separately in the postfix.
    public static class OmNomNomNomFooooood
    {
        public struct State
        {
            public float SatBefore;
            public float MaxSat;
            public float ExpectedSat;             // nutriProps.Satiety * satLossMul
            public EnumFoodCategory FoodCategory;
            public FoodNutritionProperties[] AddProps; // EFACA extras (Satiety pre-spoilage-scaled)
            public bool Valid;
        }

        /// <summary>
        /// Snapshot the food item state before <c>tryEatStop</c> runs.
        /// Reads spoilage state, mirrors the method's satiety-loss multiplier, and reads EFACA
        /// <c>expandedSats</c> from the slot <em>now</em> — the slot is cleared by <c>TakeOut(1)</c>
        /// inside the method body and would be gone by postfix time.
        /// </summary>
        public static void Prefix(CollectibleObject __instance, float secondsUsed, ItemSlot slot,
                                   EntityAgent byEntity, ref State __state)
        {
            try
            {
                __state = default;

                if (byEntity.World.Side != EnumAppSide.Server) return;
                if (secondsUsed < 0.95f) return;
                if (slot?.Itemstack == null) return;

                FoodNutritionProperties nutriProps = __instance.GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity);
                if (nutriProps == null) return;

                // Mirror tryEatStop's spoilage math exactly.
                float spoilState  = __instance.UpdateAndGetTransitionState(byEntity.World, slot, EnumTransitionType.Perish)?.TransitionLevel ?? 0f;
                float satLossMul  = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, byEntity);
                float expectedSat = nutriProps.Satiety * satLossMul;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                // EFACA: read expandedSats NOW before slot.TakeOut(1) fires inside the method.
                FoodNutritionProperties[] addProps = null;
                if (ExpandedStomachModSystem.EFACAactive)
                {
                    var raw = Helpers.GetAdditiveNutritionProperties(__instance, slot);
                    if (raw != null)
                    {
                        float spoilMult = 1f - spoilState;
                        foreach (var p in raw) p.Satiety *= spoilMult;
                        addProps = raw;
                    }
                }

                __state = new State
                {
                    SatBefore    = hunger.GetFloat("currentsaturation"),
                    MaxSat       = hunger.GetFloat("maxsaturation"),
                    ExpectedSat  = expectedSat,
                    FoodCategory = nutriProps.FoodCategory,
                    AddProps     = addProps,
                    Valid        = true
                };
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"OmNomNomNomFooooood Prefix threw: {ex}");
            }
        }

        /// <summary>
        /// After <c>tryEatStop</c> returns, absorb satiety overflow into the expanded stomach.
        /// Uses an analytical headroom calculation (not a before/after delta) so BrainFreeze's
        /// own saturation adjustments don't inflate the overflow figure.
        /// Each EFACA additive category is credited separately; the base food overflow goes to
        /// its own category. Health effects are entirely handled by the vanilla method and are
        /// never touched here.
        /// </summary>
        public static void Postfix(EntityAgent byEntity, State __state)
        {
            try
            {
                if (!__state.Valid) return;

                var hunger  = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
                var stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (hunger == null || stomach == null) return;

                // Overflow from base food: analytical headroom (not delta, so BrainFreeze's
                // own adjustments stay in its lane and don't inflate our overflow figure).
                float overflowSat = Math.Max(0f, __state.ExpectedSat - (__state.MaxSat - __state.SatBefore));

                // EFACA additives go entirely to expanded stomach regardless of base headroom.
                float efacaTotalSat = 0f;
                if (__state.AddProps != null)
                    foreach (var p in __state.AddProps) efacaTotalSat += p.Satiety;

                float totalWanted = overflowSat + efacaTotalSat;
                if (totalWanted <= 0.001f) return;

                float stomachMeter  = stomach.GetFloat("expandedStomachMeter");
                int   stomachSize   = stomach.GetInt("stomachSize");
                float expandedAvail = (float)stomachSize - stomachMeter;
                if (expandedAvail <= 0f) return;

                float satToAbsorb = Math.Min(totalWanted, expandedAvail);
                float scale        = satToAbsorb / totalWanted;

                // Base food overflow → its own nutrition category.
                if (overflowSat > 0.001f)
                    Helpers.GetNutrientsFromFoodType(__state.FoodCategory, overflowSat * scale, byEntity);

                // EFACA extras → each prop's own category (fixes old code that lumped them all
                // into the base food's category).
                if (__state.AddProps != null)
                    foreach (var p in __state.AddProps)
                        if (p.Satiety > 0.001f)
                            Helpers.GetNutrientsFromFoodType(p.FoodCategory, p.Satiety * scale, byEntity);

                stomach.SetFloat("expandedStomachMeter", stomachMeter + satToAbsorb);
                byEntity.WatchedAttributes.MarkPathDirty("expandedStomach");

                byEntity.ReceiveSaturation(0f, __state.FoodCategory);
            }
            catch (Exception ex)
            {
                ExpandedStomachModSystem.Logger.Error($"OmNomNomNomFooooood Postfix threw: {ex}");
            }
        }
    }
    #endregion

    #region EntityBehaviorBodyTemperature_OnGameTick
    public static class Patch_EntityBehaviorBodyTemperature_UpdateBodyTemperature
    {
        private static readonly AccessTools.FieldRef<EntityBehaviorBodyTemperature, float> ClothingBonusRef =
            AccessTools.FieldRefAccess<EntityBehaviorBodyTemperature, float>("clothingBonus");

        public struct State
        {
            public float SnapshotBonus;
            public float FatBonus;
            public EntityBehaviorBodyTemperature Instance;
            public bool Valid;
        }

        /// <summary>
        /// Before <c>updateBodyTemperature</c> runs, reads the fat meter from the expanded stomach
        /// and adds a fat-based clothing bonus (up to +10 °C at 100% fat) to <c>clothingBonus</c>.
        /// Snapshots the original value so the postfix can restore it precisely.
        /// </summary>
        public static void Prefix(EntityBehaviorBodyTemperature __instance, ref State __state)
        {
            try
            {
                __state = default;
                ITreeAttribute stomachTree = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (stomachTree == null) return;
                float fatMeter = stomachTree.GetFloat("fatMeter");
                if (fatMeter <= 0f) return;
                float fatBonus      = fatMeter * 10f;
                float snapshotBonus = ClothingBonusRef(__instance);
                ClothingBonusRef(__instance) = snapshotBonus + fatBonus;
                __state = new State { SnapshotBonus = snapshotBonus, FatBonus = fatBonus, Instance = __instance, Valid = true };
            }
            catch (Exception ex) { ExpandedStomachModSystem.Logger.Error($"Patch_EBBT Prefix threw: {ex}"); }
        }

        /// <summary>
        /// After <c>updateBodyTemperature</c> returns, restores <c>clothingBonus</c> to whichever is
        /// smaller: the snapshot taken in the prefix or the current value. This protects against
        /// <c>updateWearableConditions</c> running between prefix and postfix and reducing the bonus
        /// (e.g. the player removed a clothing item mid-tick).
        /// </summary>
        public static void Postfix(State __state)
        {
            try
            {
                if (!__state.Valid) return;
                float currentBonus = ClothingBonusRef(__state.Instance);
                ClothingBonusRef(__state.Instance) = Math.Min(currentBonus, __state.SnapshotBonus);
            }
            catch (Exception ex) { ExpandedStomachModSystem.Logger.Error($"Patch_EBBT Postfix threw: {ex}"); }
        }
    }
    #endregion

    #region EntityBehaviorHunger_ReduceSaturation
    public static class Patch_EntityBehaviorHunger_ReduceSaturation
    {
        /// <summary>
        /// Intercepts <c>ReduceSaturation</c> before it touches the base hunger meter.
        /// Drains the expanded stomach first using its own scaled formula (fat drawback + level scaling).
        /// If the expanded stomach can cover the full loss, <paramref name="satLossMultiplier"/> is set
        /// to zero so the original method drains nothing from base saturation.
        /// If the expanded stomach runs dry, <paramref name="satLossMultiplier"/> is reduced
        /// proportionally so the original method drains only the uncovered remainder.
        /// Mirrors the original method's saturation-loss delay logic — no expanded stomach drain
        /// occurs on ticks where any food category delay is still active.
        /// </summary>
        public static void Prefix(EntityBehaviorHunger __instance, ref float satLossMultiplier)
        {
            try
            {
                // Mirror the original method's isondelay logic: if any food category is still in
                // its saturation-loss delay window, the original skips the base Saturation drain
                // entirely. We match that — no expanded stomach drain on delay ticks either.
                if (__instance.SaturationLossDelayFruit     > 0f ||
                    __instance.SaturationLossDelayVegetable > 0f ||
                    __instance.SaturationLossDelayProtein   > 0f ||
                    __instance.SaturationLossDelayGrain     > 0f ||
                    __instance.SaturationLossDelayDairy     > 0f) return;

                var stomach = __instance.entity.WatchedAttributes.GetTreeAttribute("expandedStomach");
                if (stomach == null) return;

                float prevStomachSat = stomach.GetFloat("expandedStomachMeter");
                if (prevStomachSat <= 0f) return;

                float expandedSatLoss = satLossMultiplier * 10f
                    * ExpandedStomachModSystem.serverapi.World.Config.GetFloat("ExpandedStomach.stomachSatLossMultiplier");
                var config = ExpandedStomachModSystem.sConfig;
                expandedSatLoss *= (1f + stomach.GetFloat("fatMeter") * config.drawbackSeverity);
                // At MaxStomachSize the multiplier is 0.5 (50% faster drain). Divisor = MaxStomachSize * 2.
                expandedSatLoss *= (1f + (prevStomachSat / (EntityBehaviorStomach.MaxStomachSize * 2f)));

                float actualDrained = Math.Min(prevStomachSat, expandedSatLoss);
                stomach.SetFloat("expandedStomachMeter", prevStomachSat - actualDrained);
                __instance.entity.WatchedAttributes.MarkPathDirty("expandedStomach");

                // Scale base drain by the fraction that expanded stomach couldn't cover.
                // Full coverage → satLossMultiplier = 0 (base unchanged).
                // Partial coverage → satLossMultiplier proportionally reduced.
                if (expandedSatLoss > 0f)
                    satLossMultiplier *= Math.Max(0f, (expandedSatLoss - actualDrained) / expandedSatLoss);
                else
                    satLossMultiplier = 0f;
            }
            catch (Exception ex) { ExpandedStomachModSystem.Logger.Error($"Patch_EBH Prefix threw: {ex}"); }
        }
    }
    #endregion

    #region Helpers
    public static class Helpers
    {
        #region GetNutrientsFromMeal
        /// <summary>
        /// Credits each component of a multi-ingredient meal to its own food category in the
        /// expanded stomach's nutrition tracking, scaled by <paramref name="servingsConsumed"/>.
        /// Passes <c>wasMeal = true</c> to <see cref="GetNutrientsFromFoodType"/> so that
        /// Hydrate or Diedrate's nutrition-deficit mechanic is applied where relevant.
        /// </summary>
        public static void GetNutrientsFromMeal(FoodNutritionProperties[] foodprops, float servingsConsumed, EntityAgent byEntity)
        {
            foreach (var foodprop in foodprops)
            {
                float saturation = foodprop.Satiety * servingsConsumed;
                GetNutrientsFromFoodType(foodprop.FoodCategory, saturation, byEntity, true);
            }
            if (ExpandedStomachModSystem.EFACAactive)
            {
                ; // nothing yet. This is here as a bookmark for future expansion, just in case.
            }
        }
        #endregion

        #region GetAdditiveNutritionProperties
        /// <summary>
        /// Reads EFACA/ACA <c>expandedSats</c> float-array attributes from the item stack and
        /// returns one <see cref="FoodNutritionProperties"/> entry per non-zero category.
        /// Satiety values are pre-multiplied by the collectible's <c>satMult</c> attribute.
        /// The caller is responsible for applying the spoilage multiplier <c>(1 - spoilState)</c>
        /// before passing the results to <see cref="GetNutrientsFromFoodType"/>.
        /// Health values (<c>exSats[0]</c>) are stored on the returned props but are never
        /// converted to satiety — vanilla health effects are handled by the base method.
        /// Returns <c>null</c> if the item has no <c>expandedSats</c> attribute or fewer than
        /// six entries.
        /// </summary>
        internal static FoodNutritionProperties[] GetAdditiveNutritionProperties(CollectibleObject __instance, ItemSlot slot)
        {
            float SatMult = __instance.Attributes?["satMult"].AsFloat(1f) ?? 1f;
            FloatArrayAttribute additiveNutrients = slot.Itemstack.Attributes["expandedSats"] as FloatArrayAttribute;
            float[] exSats = additiveNutrients?.value;
            if (exSats == null || exSats.Length < 6) return null;
            List<FoodNutritionProperties> props = [];
            for (int i = 1; i <= 5; i++)
            {
                if (exSats[i] != 0)
                    props.Add(new() { FoodCategory = (EnumFoodCategory)(i - 1), Satiety = exSats[i] * SatMult });
            }
            if (exSats[0] != 0 && props.Count > 0) props[0].Health = exSats[0] * SatMult;
            return props.ToArray();
        }
        #endregion

        #region GetNutrientsFromFoodType
        /// <summary>
        /// Credits <paramref name="saturationConsumed"/> satiety to the appropriate food-category
        /// level (fruit, vegetable, protein, grain, dairy) in the hunger WatchedAttribute tree,
        /// scaled by difficulty. Only runs when the base hunger meter is at or near its maximum —
        /// this method exists solely to credit nutrition for food absorbed into the expanded stomach
        /// after the base is full.
        /// <para>
        /// <paramref name="wasMeal"/> enables Hydrate or Diedrate's nutrition-deficit reduction for
        /// meal ingredients; pass <c>false</c> for individual food items and liquids.
        /// </para>
        /// <para>
        /// <see cref="EnumFoodCategory.Unknown"/> is handled gracefully — the method returns without
        /// updating any category, which is the correct behaviour for category-less liquids such as water.
        /// </para>
        /// </summary>
        public static void GetNutrientsFromFoodType(EnumFoodCategory foodCat, float saturationConsumed, EntityAgent byEntity, bool wasMeal = false)
        {
            ITreeAttribute stomach = byEntity.WatchedAttributes.GetTreeAttribute("expandedStomach");
            if (stomach == null) return;
            bool hodactive = ExpandedStomachModSystem.HoDactive;

            ITreeAttribute hunger = byEntity.WatchedAttributes.GetTreeAttribute("hunger");
            float currentsat = hunger.GetFloat("currentsaturation");
            float maxsat = hunger.GetFloat("maxsaturation");

            ITreeAttribute thirst = byEntity.WatchedAttributes.GetTreeAttribute("thirst");
            float nutritionDeficit = thirst?.TryGetFloat("nutritionDeficitAmount") ?? 0f;
            if (currentsat >= maxsat * 0.999f)
            {
                //only absorb 1/4 of nutrition if possible. sat/10 is calculation
                // Math.Min(maxsat, nutlevel + sat/10
                float fruitsat = hunger.GetFloat("fruitLevel");
                float vegetablesat = hunger.GetFloat("vegetableLevel");
                float proteinsat = hunger.GetFloat("proteinLevel");
                float grainsat = hunger.GetFloat("grainLevel");
                float dairysat = hunger.GetFloat("dairyLevel");
                float satMult = 1f;
                //don't need to recalculate the saturation consumed
                switch (byEntity.getDifficulty())
                {
                    case "easy":
                        satMult = 0.5f;
                        break;
                    case "normal":
                        satMult = 0.25f;
                        break;
                    case "hard":
                        satMult = 0.15f;
                        break;
                }
                switch (foodCat)
                {
                    case EnumFoodCategory.Fruit:
                        if (hodactive && wasMeal)
                        {
                            //apply fraction to hoD
                            nutritionDeficit -= saturationConsumed;
                            nutritionDeficit = Math.Max(0f, nutritionDeficit);
                            thirst.SetFloat("nutritionDeficitAmount", nutritionDeficit);
                            byEntity.WatchedAttributes.MarkPathDirty("thirst");
                        }
                        fruitsat = Math.Min(maxsat, fruitsat + saturationConsumed * satMult);
                        hunger.SetFloat("fruitLevel", fruitsat);
                        break;
                    case EnumFoodCategory.Vegetable:
                        if (hodactive && wasMeal)
                        {
                            //apply fraction to hoD
                            nutritionDeficit -= saturationConsumed;
                            nutritionDeficit = Math.Max(0f, nutritionDeficit);
                            thirst.SetFloat("nutritionDeficitAmount", nutritionDeficit);
                            byEntity.WatchedAttributes.MarkPathDirty("thirst");
                        }
                        vegetablesat = Math.Min(maxsat, vegetablesat + saturationConsumed * satMult);
                        hunger.SetFloat("vegetableLevel", vegetablesat);
                        break;
                    case EnumFoodCategory.Protein:
                        if (hodactive && wasMeal)
                        {
                            //apply fraction to hoD
                            nutritionDeficit -= saturationConsumed;
                            nutritionDeficit = Math.Max(0f, nutritionDeficit);
                            thirst.SetFloat("nutritionDeficitAmount", nutritionDeficit);
                            byEntity.WatchedAttributes.MarkPathDirty("thirst");
                        }
                        proteinsat = Math.Min(maxsat, proteinsat + saturationConsumed * satMult);
                        hunger.SetFloat("proteinLevel", proteinsat);
                        break;
                    case EnumFoodCategory.Grain:
                        if (hodactive && wasMeal)
                        {
                            //apply fraction to hoD
                            nutritionDeficit -= saturationConsumed;
                            nutritionDeficit = Math.Max(0f, nutritionDeficit);
                            thirst.SetFloat("nutritionDeficitAmount", nutritionDeficit);
                            byEntity.WatchedAttributes.MarkPathDirty("thirst");
                        }
                        grainsat = Math.Min(maxsat, grainsat + saturationConsumed * satMult);
                        hunger.SetFloat("grainLevel", grainsat);
                        break;
                    case EnumFoodCategory.Dairy:
                        if (hodactive && wasMeal)
                        {
                            //apply fraction to hoD
                            nutritionDeficit -= saturationConsumed;
                            nutritionDeficit = Math.Max(0f, nutritionDeficit);
                            thirst.SetFloat("nutritionDeficitAmount", nutritionDeficit);
                            byEntity.WatchedAttributes.MarkPathDirty("thirst");
                        }
                        dairysat = Math.Min(maxsat, dairysat + saturationConsumed * satMult);
                        hunger.SetFloat("dairyLevel", dairysat);
                        break;
                    default:
                        ; // do nothing. We couldn't update any saturation values because no category was specified
                        break;
                }
                byEntity.WatchedAttributes.SetAttribute("hunger", hunger);
                byEntity.WatchedAttributes.MarkPathDirty("hunger");
            }
        }
        #endregion

    }
    #endregion

}
